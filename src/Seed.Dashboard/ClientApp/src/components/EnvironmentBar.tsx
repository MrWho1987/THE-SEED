import type { WorldFrameDto, SimulationStatusDto } from '../types';

interface Props {
  frame: WorldFrameDto | null;
  status: SimulationStatusDto | null;
}

export function EnvironmentBar({ frame, status }: Props) {
  if (!frame) return null;

  const light = frame.lightLevel ?? 1;
  const lightPct = Math.round(light * 100);
  const mult = frame.foodEnergyMultiplier ?? 1;

  let season: string;
  let seasonColor: string;
  if (mult > 1.15) {
    season = 'Summer';
    seasonColor = 'var(--color-season-summer)';
  } else if (mult > 1.0) {
    season = 'Spring';
    seasonColor = 'var(--color-season-spring)';
  } else if (mult > 0.85) {
    season = 'Fall';
    seasonColor = 'var(--color-season-fall)';
  } else {
    season = 'Winter';
    seasonColor = 'var(--color-season-winter)';
  }

  const alive = frame.agents.filter(a => a.alive).length;
  const dead = frame.agents.length - alive;
  const total = frame.agents.length;
  const alivePct = total > 0 ? (alive / total) * 100 : 0;

  const tick = status?.currentTick ?? 0;
  const maxTicks = status?.maxTicksPerRound ?? 1;
  const tickPct = Math.min((tick / maxTicks) * 100, 100);

  const iconBg = `hsl(${Math.round(45 + (1 - light) * 175)}, ${Math.round(30 + light * 50)}%, ${Math.round(20 + light * 50)}%)`;

  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5 px-3 py-2 bg-[var(--color-surface)] rounded border border-[var(--color-border)] text-xs font-mono">
      <div className="flex items-center gap-1.5">
        <div
          className="w-4 h-4 rounded-full border border-[var(--color-border)]"
          style={{ backgroundColor: iconBg }}
        />
        <span className="text-[var(--color-text-muted)]">Light:</span>
        <span className="text-[var(--color-text)]">{lightPct}%</span>
      </div>

      <div className="hidden sm:block w-px h-4 bg-[var(--color-border)]" />

      <div className="flex items-center gap-1.5">
        <span
          className="inline-block w-2 h-2 rounded-full"
          style={{ backgroundColor: seasonColor }}
        />
        <span className="text-[var(--color-text-muted)]">{season}</span>
        <span className="text-[var(--color-text)]">×{mult.toFixed(2)}</span>
      </div>

      <div className="hidden sm:block w-px h-4 bg-[var(--color-border)]" />

      <div className="flex items-center gap-1.5">
        <span className="text-[var(--color-text-muted)]">Food:</span>
        <span className="text-[var(--color-success)]">{frame.food.length}</span>
      </div>

      <div className="hidden sm:block w-px h-4 bg-[var(--color-border)]" />

      <div className="flex items-center gap-2 basis-full sm:basis-auto sm:flex-1 min-w-0">
        <span className="text-[var(--color-text-muted)] shrink-0">Pop:</span>
        <div className="flex-1 h-2 bg-[#374151] rounded-full overflow-hidden min-w-[40px]">
          <div
            className="h-full bg-[var(--color-success)] rounded-full transition-all duration-300"
            style={{ width: `${alivePct}%` }}
          />
        </div>
        <span className="text-[var(--color-success)] shrink-0">{alive}</span>
        <span className="text-[var(--color-text-muted)] shrink-0">/</span>
        <span className="text-[var(--color-text-muted)] shrink-0">{dead}☠</span>
      </div>

      <div className="hidden sm:block w-px h-4 bg-[var(--color-border)]" />

      <div className="flex items-center gap-2 basis-full sm:basis-auto sm:min-w-[120px]">
        <span className="text-[var(--color-text-muted)] shrink-0">Tick:</span>
        <div className="flex-1 h-1.5 bg-[#374151] rounded-full overflow-hidden">
          <div
            className="h-full bg-[var(--color-primary)] rounded-full transition-all duration-150"
            style={{ width: `${tickPct}%` }}
          />
        </div>
        <span className="text-[var(--color-text)] shrink-0">{tick}/{maxTicks}</span>
      </div>

      <div className="hidden sm:block w-px h-4 bg-[var(--color-border)]" />

      <div className="flex items-center gap-1.5">
        <span className="text-[var(--color-text-muted)]">Round:</span>
        <span className="text-[var(--color-text)]">
          {(status?.currentRound ?? 0) + 1}/{status?.arenaRounds ?? 4}
        </span>
      </div>

      <div className="hidden sm:block w-px h-4 bg-[var(--color-border)]" />

      <div className="flex items-center gap-3 text-[var(--color-text-muted)]">
        <span>Gen <strong className="text-[var(--color-text)]">{status?.currentGeneration ?? 0}</strong></span>
        <span>Species <strong className="text-[var(--color-text)]">{status?.speciesCount ?? 0}</strong></span>
      </div>

      {status?.overridesActive && (
        <span className="px-1.5 py-0.5 text-[9px] rounded font-mono bg-[var(--color-warning)]/20 text-[var(--color-warning)]">
          OVERRIDES
        </span>
      )}
    </div>
  );
}
