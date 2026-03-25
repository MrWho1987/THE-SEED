import { useState } from 'react';
import type { AgentDto, SelectedAgentDetailsDto } from '../types';

interface Props {
  agent: AgentDto | null;
  speciesId?: number;
  details: SelectedAgentDetailsDto | null;
}

type Tab = 'live' | 'genome' | 'rounds';

function Bar({ value, max, color }: { value: number; max: number; color: string }) {
  const pct = Math.max(0, Math.min(100, (value / max) * 100));
  return (
    <div className="flex-1 h-2 bg-[#1f2937] rounded-full overflow-hidden">
      <div
        className="h-full rounded-full transition-all duration-200"
        style={{ width: `${pct}%`, backgroundColor: color }}
      />
    </div>
  );
}

function SignalBar({ value }: { value: number }) {
  const norm = (value + 1) / 2;
  const hue = norm * 120;
  return (
    <div className="flex-1 h-2 bg-[#1f2937] rounded-full overflow-hidden relative">
      <div className="absolute left-1/2 top-0 w-px h-full bg-[#4b5563]" />
      <div
        className="h-full rounded-full transition-all duration-200 absolute"
        style={{
          left: value >= 0 ? '50%' : `${norm * 100}%`,
          width: `${Math.abs(value) * 50}%`,
          backgroundColor: `hsl(${hue}, 70%, 50%)`,
        }}
      />
    </div>
  );
}

function ModulatorBar({ label, value, color }: { label: string; value: number; color: string }) {
  const pct = Math.max(0, Math.min(100, value * 100));
  return (
    <div className="flex items-center gap-2">
      <span className="text-[var(--color-text-muted)] w-14 shrink-0">{label}</span>
      <div className="flex-1 h-1.5 bg-[#1f2937] rounded-full overflow-hidden">
        <div className="h-full rounded-full" style={{ width: `${pct}%`, backgroundColor: color }} />
      </div>
      <span className="text-[var(--color-text)] w-10 text-right">{value.toFixed(3)}</span>
    </div>
  );
}

function LiveTab({ agent, validDetails }: { agent: AgentDto; validDetails: SelectedAgentDetailsDto | null }) {
  const energyBarFrac = 1 - 1 / (1 + agent.energy);
  const eHue = Math.floor(Math.min(energyBarFrac * 1.5, 1) * 120);
  const energyColor = `hsl(${eHue}, 70%, 50%)`;
  const energyBarPct = Math.max(0, Math.min(100, energyBarFrac * 100));
  const speedVal = Math.abs(agent.speed);
  const speedMax = 2.0;
  const headingDeg = Math.round(((agent.heading * 180) / Math.PI + 360) % 360);

  return (
    <>
      <div>
        <div className="flex items-center justify-between mb-1">
          <span className="text-[var(--color-text-muted)]">Energy</span>
          <span style={{ color: energyColor }}>{agent.energy.toFixed(2)}</span>
        </div>
        <div className="h-3 bg-[#1f2937] rounded-full overflow-hidden">
          <div
            className="h-full rounded-full transition-all duration-300"
            style={{
              width: `${energyBarPct}%`,
              background: `linear-gradient(90deg, hsl(0, 70%, 50%), hsl(60, 70%, 50%) 50%, hsl(120, 70%, 50%))`,
              backgroundSize: '100% 100%',
              backgroundPosition: `${100 - energyBarPct}% 0`,
            }}
          />
        </div>
      </div>

      <div className="grid grid-cols-2 gap-x-4 gap-y-2">
        <div>
          <span className="text-[var(--color-text-muted)]">Speed {agent.speed < -0.01 ? '(rev)' : ''}</span>
          <div className="flex items-center gap-2 mt-0.5">
            <Bar value={speedVal} max={speedMax} color="#06b6d4" />
            <span className="text-[var(--color-text)] w-8 text-right">{agent.speed.toFixed(2)}</span>
          </div>
        </div>
        <div>
          <span className="text-[var(--color-text-muted)]">Heading</span>
          <div className="flex items-center gap-2 mt-0.5">
            <div className="w-5 h-5 border border-[var(--color-border)] rounded-full relative flex items-center justify-center">
              <div
                className="absolute w-0.5 h-2 bg-[var(--color-primary)] rounded origin-bottom"
                style={{ transform: `rotate(${headingDeg}deg)`, bottom: '50%' }}
              />
            </div>
            <span className="text-[var(--color-text)]">{headingDeg}°</span>
          </div>
        </div>
        <div>
          <span className="text-[var(--color-text-muted)]">Signal 0</span>
          <div className="flex items-center gap-2 mt-0.5">
            <SignalBar value={agent.signal0} />
            <span className="text-[var(--color-text)] w-8 text-right">{agent.signal0.toFixed(2)}</span>
          </div>
        </div>
        <div>
          <span className="text-[var(--color-text-muted)]">Signal 1</span>
          <div className="flex items-center gap-2 mt-0.5">
            <SignalBar value={agent.signal1} />
            <span className="text-[var(--color-text)] w-8 text-right">{agent.signal1.toFixed(2)}</span>
          </div>
        </div>
      </div>

      <div className="flex gap-3 pt-1 border-t border-[var(--color-border)] min-h-[24px]">
        <div className="flex items-center gap-1">
          <span className={agent.shareReceived > 0.001 ? 'text-[var(--color-success)]' : 'text-[var(--color-text-muted)]/30'}>↓</span>
          <span className="text-[var(--color-text-muted)]">Share:</span>
          <span className={agent.shareReceived > 0.001 ? 'text-[var(--color-success)]' : 'text-[var(--color-text-muted)]/30'}>
            {agent.shareReceived > 0.001 ? `+${agent.shareReceived.toFixed(3)}` : '---'}
          </span>
        </div>
        <div className="flex items-center gap-1">
          <span className={agent.attackReceived > 0.001 ? 'text-[var(--color-danger)]' : 'text-[var(--color-text-muted)]/30'}>⚔</span>
          <span className="text-[var(--color-text-muted)]">Attack:</span>
          <span className={agent.attackReceived > 0.001 ? 'text-[var(--color-danger)]' : 'text-[var(--color-text-muted)]/30'}>
            {agent.attackReceived > 0.001 ? `-${agent.attackReceived.toFixed(3)}` : '---'}
          </span>
        </div>
      </div>

      {validDetails && (
        <>
          <div className="grid grid-cols-2 gap-x-4 gap-y-1 pt-1 border-t border-[var(--color-border)]">
            <div className="flex justify-between">
              <span className="text-[var(--color-text-muted)]">Survival</span>
              <span className="text-[var(--color-text)]">{validDetails.survivalTicks}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-[var(--color-text-muted)]">Food</span>
              <span className="text-[var(--color-success)]">{validDetails.foodCollected}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-[var(--color-text-muted)]">Distance</span>
              <span className="text-[var(--color-text)]">{validDetails.distanceTraveled.toFixed(1)}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-[var(--color-text-muted)]">ΔEnergy</span>
              <span className={validDetails.netEnergyDelta >= 0 ? 'text-[var(--color-success)]' : 'text-[var(--color-danger)]'}>
                {validDetails.netEnergyDelta >= 0 ? '+' : ''}{validDetails.netEnergyDelta.toFixed(3)}
              </span>
            </div>
          </div>

          <div className="flex flex-col gap-1 pt-1 border-t border-[var(--color-border)]">
            <span className="text-[var(--color-text-muted)] text-[10px] uppercase tracking-wider">Modulators</span>
            <ModulatorBar label="Reward" value={validDetails.modReward} color="#10b981" />
            <ModulatorBar label="Pain" value={validDetails.modPain} color="#ef4444" />
            <ModulatorBar label="Curiosity" value={validDetails.modCuriosity} color="#a78bfa" />
          </div>
        </>
      )}
    </>
  );
}

function GenomeTab({ validDetails, speciesId }: { validDetails: SelectedAgentDetailsDto | null; speciesId?: number }) {
  if (!validDetails) {
    return <div className="text-[var(--color-text-muted)] text-center py-4">Loading...</div>;
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="grid grid-cols-2 gap-x-4 gap-y-2">
        <div className="flex justify-between">
          <span className="text-[var(--color-text-muted)]">Connections</span>
          <span className="text-[var(--color-accent)]">{validDetails.connectionCount}</span>
        </div>
        <div className="flex justify-between">
          <span className="text-[var(--color-text-muted)]">Total Nodes</span>
          <span className="text-[var(--color-text)]">{validDetails.totalNodeCount}</span>
        </div>
        <div className="flex justify-between">
          <span className="text-[var(--color-text-muted)]">Hidden Nodes</span>
          <span className="text-[var(--color-accent)]">{validDetails.hiddenNodeCount}</span>
        </div>
        <div className="flex justify-between">
          <span className="text-[var(--color-text-muted)]">Species</span>
          <span className="text-[var(--color-text)]">{speciesId ?? '?'}</span>
        </div>
      </div>
      <div className="flex justify-between pt-2 border-t border-[var(--color-border)]">
        <span className="text-[var(--color-text-muted)]">Instability</span>
        <span className="text-[var(--color-warning)]">{validDetails.instabilityPenalty.toFixed(4)}</span>
      </div>
      <div className="flex justify-between">
        <span className="text-[var(--color-text-muted)]">Aggregated Fitness</span>
        <span className="text-[var(--color-success)] font-semibold">
          {validDetails.aggregatedFitness != null ? validDetails.aggregatedFitness.toFixed(2) : '---'}
        </span>
      </div>
    </div>
  );
}

function RoundsTab({ validDetails }: { validDetails: SelectedAgentDetailsDto | null }) {
  if (!validDetails) {
    return <div className="text-[var(--color-text-muted)] text-center py-4">Loading...</div>;
  }

  const rounds = validDetails.roundHistory;
  if (rounds.length === 0) {
    return <div className="text-[var(--color-text-muted)] text-center py-4">Round in progress...</div>;
  }

  const maxFitness = Math.max(...rounds.map(r => Math.abs(r.fitness)), 0.01);

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-end gap-1 h-16">
        {rounds.map((r) => {
          const h = Math.max(4, (Math.abs(r.fitness) / maxFitness) * 56);
          const hue = r.fitness >= 0
            ? Math.min(120, (r.fitness / maxFitness) * 120)
            : 0;
          return (
            <div key={r.round} className="flex flex-col items-center flex-1 justify-end h-full">
              <div
                className="w-full rounded-t"
                style={{ height: `${h}px`, backgroundColor: `hsl(${hue}, 70%, 50%)` }}
                title={`R${r.round + 1}: ${r.fitness.toFixed(2)}`}
              />
              <span className="text-[9px] text-[var(--color-text-muted)] mt-0.5">R{r.round + 1}</span>
            </div>
          );
        })}
      </div>

      <div className="overflow-x-auto">
        <table className="w-full text-[10px]">
          <thead>
            <tr className="text-[var(--color-text-muted)]">
              <th className="text-left font-normal pr-2">Rnd</th>
              <th className="text-right font-normal pr-2">Surv</th>
              <th className="text-right font-normal pr-2">Food</th>
              <th className="text-right font-normal pr-2">ΔE</th>
              <th className="text-right font-normal pr-2">Dist</th>
              <th className="text-right font-normal">Fit</th>
            </tr>
          </thead>
          <tbody>
            {rounds.map((r) => (
              <tr key={r.round} className="text-[var(--color-text)]">
                <td className="pr-2">{r.round + 1}</td>
                <td className="text-right pr-2">{r.survivalTicks}</td>
                <td className="text-right pr-2">{r.foodCollected}</td>
                <td className={`text-right pr-2 ${r.netEnergyDelta >= 0 ? 'text-[var(--color-success)]' : 'text-[var(--color-danger)]'}`}>
                  {r.netEnergyDelta.toFixed(2)}
                </td>
                <td className="text-right pr-2">{r.distanceTraveled.toFixed(1)}</td>
                <td className="text-right font-semibold">{r.fitness.toFixed(2)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

export function AgentInspector({ agent, speciesId, details }: Props) {
  const [activeTab, setActiveTab] = useState<Tab>('live');

  if (!agent) {
    return (
      <div className="p-4 text-center text-[var(--color-text-muted)] text-sm">
        Click an agent in the world to inspect it
      </div>
    );
  }

  const validDetails = (details && details.agentId === agent.id) ? details : null;
  const speciesHue = ((speciesId ?? 0) * 137.5) % 360;
  const speciesColor = `hsl(${speciesHue}, 70%, 55%)`;

  const tabs: { id: Tab; label: string }[] = [
    { id: 'live', label: 'Live' },
    { id: 'genome', label: 'Genome' },
    { id: 'rounds', label: 'Rounds' },
  ];

  return (
    <div className="flex flex-col gap-3 p-3 text-xs font-mono">
      <div className="flex items-center gap-2">
        <div
          className="w-3 h-3 rounded-full border border-white/20"
          style={{ backgroundColor: speciesColor }}
        />
        <span className="font-semibold text-sm text-[var(--color-text)]">
          Agent #{agent.id}
        </span>
        <span className="text-[var(--color-text-muted)]">
          Sp. {speciesId ?? '?'}
        </span>
        <span className={`ml-auto px-1.5 py-0.5 rounded text-[10px] font-bold ${
          agent.alive
            ? 'bg-[var(--color-success)]/20 text-[var(--color-success)]'
            : 'bg-[var(--color-danger)]/20 text-[var(--color-danger)]'
        }`}>
          {agent.alive ? 'ALIVE' : 'DEAD'}
        </span>
      </div>

      <div className="flex gap-1">
        {tabs.map(tab => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
            className={`px-2.5 py-1 text-[10px] rounded transition-colors ${
              activeTab === tab.id
                ? 'bg-[var(--color-accent)] text-black font-semibold'
                : 'bg-[var(--color-surface-alt)] text-[var(--color-text-muted)] hover:text-[var(--color-text)]'
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {activeTab === 'live' && <LiveTab agent={agent} validDetails={validDetails} />}
      {activeTab === 'genome' && <GenomeTab validDetails={validDetails} speciesId={speciesId} />}
      {activeTab === 'rounds' && <RoundsTab validDetails={validDetails} />}
    </div>
  );
}
