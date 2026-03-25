import { useState, useCallback, useRef, useEffect } from 'react';
import type { SimulationStatusDto, WorldOverrideDto } from '../types';
import type { SimulationControls } from '../useSignalR';

interface Props {
  status: SimulationStatusDto | null;
  controls: SimulationControls;
  initialOverrides?: WorldOverrideDto | null;
}

interface SliderDef {
  key: keyof WorldOverrideDto;
  label: string;
  tip: string;
  min: number;
  max: number;
  step: number;
  defaultValue: number;
  format: (v: number) => string;
}

function InfoTip({ text }: { text: string }) {
  return (
    <span className="relative group/tip shrink-0">
      <span className="inline-flex items-center justify-center w-3 h-3 rounded-full border border-[var(--color-text-muted)]/40 text-[7px] leading-none text-[var(--color-text-muted)] cursor-help select-none">
        i
      </span>
      <span className="pointer-events-none absolute bottom-full left-1/2 -translate-x-1/2 mb-1.5 w-48 px-2 py-1.5 rounded text-[9px] leading-tight bg-[var(--color-surface)] border border-[var(--color-border)] text-[var(--color-text)] shadow-lg opacity-0 group-hover/tip:opacity-100 transition-opacity z-50 whitespace-normal">
        {text}
      </span>
    </span>
  );
}

const ENERGY_SLIDERS: SliderDef[] = [
  { key: 'foodCount', label: 'Food Count', tip: 'Number of food items in the arena. More food means easier survival and less competitive pressure.', min: 2, max: 80, step: 1, defaultValue: 20, format: v => String(Math.round(v)) },
  { key: 'ambientEnergyRate', label: 'Ambient Energy', tip: 'Passive energy agents receive each tick from the environment. Higher values reduce the pressure to actively forage.', min: 0, max: 0.001, step: 0.00005, defaultValue: 0.00015, format: v => v.toFixed(5) },
  { key: 'corpseEnergyBase', label: 'Corpse Energy', tip: 'Energy released when an agent dies, available for decomposers. Higher values incentivize attack-based strategies.', min: 0, max: 1.0, step: 0.05, defaultValue: 0.3, format: v => v.toFixed(2) },
  { key: 'foodQualityVariation', label: 'Food Quality Var', tip: 'Random variation in food energy value. Higher means more unpredictable foraging rewards.', min: 0, max: 0.5, step: 0.05, defaultValue: 0.1, format: v => v.toFixed(2) },
];

const ENV_SLIDERS: SliderDef[] = [
  { key: 'dayNightPeriod', label: 'Day/Night Period', tip: 'Ticks per full day/night cycle. Affects light level and photosynthesis. Set to 0 for permanent daylight.', min: 0, max: 1000, step: 10, defaultValue: 150, format: v => v === 0 ? 'Always Day' : String(Math.round(v)) },
  { key: 'seasonPeriod', label: 'Season Period', tip: 'Ticks per full seasonal cycle. Seasons modulate food availability and ambient conditions. Set to 0 to disable.', min: 0, max: 5000, step: 50, defaultValue: 1500, format: v => v === 0 ? 'None' : String(Math.round(v)) },
  { key: 'hazardDamageMultiplier', label: 'Hazard Damage', tip: 'Multiplier applied to hazard zone damage. Higher values make hazard tiles significantly more dangerous.', min: 0, max: 5.0, step: 0.1, defaultValue: 1.0, format: v => v.toFixed(1) + 'x' },
];

const PRESETS: { label: string; tip: string; dto: WorldOverrideDto | null; warn?: boolean }[] = [
  { label: 'Famine', tip: 'Minimal food, no ambient energy. Tests survival under extreme scarcity.', dto: { foodCount: 5, ambientEnergyRate: 0 }, warn: true },
  { label: 'Abundance', tip: 'Plentiful food and high ambient energy. Low survival pressure.', dto: { foodCount: 50, ambientEnergyRate: 0.0008 } },
  { label: 'Eternal Night', tip: 'Near-zero light level. Tests agents\' non-visual navigation abilities.', dto: { lightLevelOverride: 0.05 }, warn: true },
  { label: 'Harsh', tip: 'Low food combined with triple hazard damage. Only the fittest survive.', dto: { foodCount: 8, hazardDamageMultiplier: 3.0 }, warn: true },
  { label: 'No Seasons', tip: 'Disables seasonal food variation for stable, predictable conditions.', dto: { seasonPeriod: 0 } },
  { label: 'Default', tip: 'Resets all overrides back to the simulation\'s original parameters.', dto: null },
];

function getDefaults(): Record<string, number> {
  const d: Record<string, number> = {};
  for (const s of [...ENERGY_SLIDERS, ...ENV_SLIDERS]) d[s.key] = s.defaultValue;
  return d;
}

export function WorldControls({ status, controls, initialOverrides }: Props) {
  const [collapsed, setCollapsed] = useState(true);
  const [values, setValues] = useState<Record<string, number>>(getDefaults);
  const [lightAuto, setLightAuto] = useState(true);
  const [lightValue, setLightValue] = useState(1.0);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const activePresetRef = useRef<string | null>(null);
  const hydratedRef = useRef(false);

  useEffect(() => {
    if (hydratedRef.current || !initialOverrides) return;
    hydratedRef.current = true;

    const newVals = { ...getDefaults() };
    for (const s of [...ENERGY_SLIDERS, ...ENV_SLIDERS]) {
      const v = (initialOverrides as Record<string, number | null | undefined>)[s.key];
      if (v != null) newVals[s.key] = v;
    }
    setValues(newVals);

    if (initialOverrides.lightLevelOverride != null) {
      setLightAuto(false);
      setLightValue(initialOverrides.lightLevelOverride);
    }
  }, [initialOverrides]);

  const buildDto = useCallback((): WorldOverrideDto => {
    const dto: WorldOverrideDto = {};
    for (const s of [...ENERGY_SLIDERS, ...ENV_SLIDERS]) {
      const v = values[s.key];
      if (v !== undefined) (dto as Record<string, number>)[s.key] = v;
    }
    if (!lightAuto) dto.lightLevelOverride = lightValue;
    return dto;
  }, [values, lightAuto, lightValue]);

  const sendOverride = useCallback((dto: WorldOverrideDto) => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      controls.applyWorldOverride(dto);
    }, 50);
  }, [controls]);

  const handleSliderChange = useCallback((key: string, val: number) => {
    activePresetRef.current = null;
    setValues(prev => {
      const next = { ...prev, [key]: val };
      const dto: WorldOverrideDto = {};
      for (const s of [...ENERGY_SLIDERS, ...ENV_SLIDERS]) {
        const v = next[s.key];
        if (v !== undefined) (dto as Record<string, number>)[s.key] = v;
      }
      if (!lightAuto) dto.lightLevelOverride = lightValue;
      sendOverride(dto);
      return next;
    });
  }, [lightAuto, lightValue, sendOverride]);

  const handleLightChange = useCallback((val: number) => {
    activePresetRef.current = null;
    setLightValue(val);
    const dto = buildDto();
    dto.lightLevelOverride = val;
    sendOverride(dto);
  }, [buildDto, sendOverride]);

  const handleLightAutoToggle = useCallback((auto: boolean) => {
    activePresetRef.current = null;
    setLightAuto(auto);
    if (auto) {
      const dto = buildDto();
      delete dto.lightLevelOverride;
      sendOverride(dto);
    } else {
      const dto = buildDto();
      dto.lightLevelOverride = lightValue;
      sendOverride(dto);
    }
  }, [buildDto, lightValue, sendOverride]);

  const handlePreset = useCallback((preset: typeof PRESETS[number]) => {
    if (preset.dto === null) {
      controls.clearWorldOverride();
      setValues(getDefaults());
      setLightAuto(true);
      setLightValue(1.0);
      activePresetRef.current = 'Default';
      return;
    }
    activePresetRef.current = preset.label;
    const newVals = { ...getDefaults() };
    for (const [k, v] of Object.entries(preset.dto)) {
      if (k === 'lightLevelOverride') {
        setLightAuto(false);
        setLightValue(v as number);
      } else {
        newVals[k] = v as number;
      }
    }
    setValues(newVals);
    sendOverride(preset.dto);
  }, [controls, sendOverride]);

  useEffect(() => {
    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, []);

  const renderSlider = (s: SliderDef) => (
    <div key={s.key} className="flex items-center gap-2">
      <span className="text-[10px] text-[var(--color-text-muted)] w-28 shrink-0 flex items-center justify-end gap-1">
        {s.label}
        <InfoTip text={s.tip} />
      </span>
      <input
        type="range"
        min={s.min}
        max={s.max}
        step={s.step}
        value={values[s.key] ?? s.defaultValue}
        onChange={e => handleSliderChange(s.key, parseFloat(e.target.value))}
        className="flex-1 h-1 accent-[var(--color-accent)]"
      />
      <span className="text-[10px] font-mono w-16 text-right">{s.format(values[s.key] ?? s.defaultValue)}</span>
    </div>
  );

  return (
    <div className="border-b border-[var(--color-border)]">
      <div
        className="px-4 py-2 flex items-center gap-3 cursor-pointer select-none hover:bg-[var(--color-surface-alt)]/30 transition-colors"
        onClick={() => setCollapsed(!collapsed)}
      >
        <span className="text-[10px] text-[var(--color-text-muted)]">{collapsed ? '▶' : '▼'}</span>
        <span className="text-xs font-semibold tracking-wider text-[var(--color-text-muted)]">WORLD CONTROLS</span>
        {status?.overridesActive && (
          <span className="px-1.5 py-0.5 text-[9px] rounded font-mono bg-[var(--color-warning)]/20 text-[var(--color-warning)]">
            OVERRIDES ACTIVE
          </span>
        )}
        <div className="ml-auto">
          <button
            onClick={e => { e.stopPropagation(); handlePreset(PRESETS[PRESETS.length - 1]); }}
            className="px-2 py-0.5 text-[10px] rounded bg-[var(--color-surface-alt)] hover:bg-[var(--color-border)] transition-colors"
          >
            Reset All
          </button>
        </div>
      </div>

      {!collapsed && (
        <div className="px-4 pb-3 space-y-3">
          {/* Presets */}
          <div className="flex gap-1.5 flex-wrap">
            {PRESETS.map(p => (
              <button
                key={p.label}
                title={p.tip}
                onClick={() => handlePreset(p)}
                className={`px-2.5 py-1 text-[10px] rounded transition-colors ${
                  activePresetRef.current === p.label
                    ? 'bg-[var(--color-accent)] text-black font-semibold'
                    : p.warn
                      ? 'bg-[var(--color-warning)]/15 text-[var(--color-warning)] hover:bg-[var(--color-warning)]/25'
                      : 'bg-[var(--color-surface-alt)] hover:bg-[var(--color-border)]'
                }`}
              >
                {p.label}
              </button>
            ))}
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-x-6 gap-y-1">
            {/* Energy Economy */}
            <div>
              <div className="text-[9px] font-semibold tracking-wider text-[var(--color-text-muted)] mb-1 uppercase">Energy Economy</div>
              {ENERGY_SLIDERS.map(renderSlider)}
            </div>

            {/* Environmental */}
            <div>
              <div className="text-[9px] font-semibold tracking-wider text-[var(--color-text-muted)] mb-1 uppercase">Environmental</div>

              {/* Light Level with Auto toggle */}
              <div className="flex items-center gap-2">
                <span className="text-[10px] text-[var(--color-text-muted)] w-28 shrink-0 flex items-center justify-end gap-1">
                  Light Level
                  <InfoTip text="Override the day/night light cycle. When Auto is on, light follows the natural day/night rhythm." />
                </span>
                <label className="flex items-center gap-1 shrink-0">
                  <input
                    type="checkbox"
                    checked={lightAuto}
                    onChange={e => handleLightAutoToggle(e.target.checked)}
                    className="accent-[var(--color-accent)] w-3 h-3"
                  />
                  <span className="text-[9px] text-[var(--color-text-muted)]">Auto</span>
                </label>
                <input
                  type="range"
                  min={0}
                  max={1}
                  step={0.05}
                  value={lightValue}
                  disabled={lightAuto}
                  onChange={e => handleLightChange(parseFloat(e.target.value))}
                  className="flex-1 h-1 accent-[var(--color-accent)] disabled:opacity-30"
                />
                <span className="text-[10px] font-mono w-16 text-right">
                  {lightAuto ? 'Auto' : lightValue.toFixed(2)}
                </span>
              </div>

              {ENV_SLIDERS.map(renderSlider)}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
