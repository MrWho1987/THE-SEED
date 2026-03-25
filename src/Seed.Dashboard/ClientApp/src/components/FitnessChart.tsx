import { useState } from 'react';
import { LineChart, Line, AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer, Brush } from 'recharts';
import type { GenerationStatsDto } from '../types';

interface Props {
  history: GenerationStatsDto[];
}

type Tab = 'fitness' | 'behavior' | 'evolution' | 'species';

const tabs: { id: Tab; label: string }[] = [
  { id: 'fitness', label: 'Fitness' },
  { id: 'behavior', label: 'Behavior' },
  { id: 'evolution', label: 'Evolution' },
  { id: 'species', label: 'Species' },
];

const tooltipStyle = {
  backgroundColor: '#12182b',
  border: '1px solid #2a3654',
  borderRadius: '8px',
};

function FitnessTab({ history }: { history: GenerationStatsDto[] }) {
  return (
    <ResponsiveContainer width="100%" height={250}>
      <LineChart data={history} margin={{ top: 5, right: 30, left: 20, bottom: 5 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="#2a3654" />
        <XAxis dataKey="generation" stroke="#94a3b8" fontSize={12} />
        <YAxis stroke="#94a3b8" fontSize={12} />
        <Tooltip contentStyle={tooltipStyle} labelStyle={{ color: '#e2e8f0' }} />
        <Legend />
        <Line type="monotone" dataKey="bestFitness" name="Best" stroke="#10b981" strokeWidth={2} dot={false} />
        <Line type="monotone" dataKey="meanFitness" name="Mean" stroke="#06b6d4" strokeWidth={2} dot={false} />
        <Line type="monotone" dataKey="worstFitness" name="Worst" stroke="#ef4444" strokeWidth={1} dot={false} strokeDasharray="5 5" />
        {history.length > 50 && <Brush dataKey="generation" height={20} stroke="var(--color-accent)" fill="var(--color-surface)" />}
      </LineChart>
    </ResponsiveContainer>
  );
}

function BehaviorTab({ history }: { history: GenerationStatsDto[] }) {
  return (
    <ResponsiveContainer width="100%" height={250}>
      <LineChart data={history} margin={{ top: 5, right: 30, left: 20, bottom: 5 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="#2a3654" />
        <XAxis dataKey="generation" stroke="#94a3b8" fontSize={12} />
        <YAxis stroke="#94a3b8" fontSize={12} />
        <Tooltip contentStyle={tooltipStyle} labelStyle={{ color: '#e2e8f0' }} />
        <Legend />
        <Line type="monotone" dataKey="avgDistanceTraveled" name="Avg Distance" stroke="#06b6d4" strokeWidth={2} dot={false} />
        <Line type="monotone" dataKey="avgFoodCollected" name="Avg Food" stroke="#10b981" strokeWidth={2} dot={false} />
        <Line type="monotone" dataKey="avgSurvivalTicks" name="Avg Survival" stroke="#f59e0b" strokeWidth={2} dot={false} />
        {history.length > 50 && <Brush dataKey="generation" height={20} stroke="var(--color-accent)" fill="var(--color-surface)" />}
      </LineChart>
    </ResponsiveContainer>
  );
}

function EvolutionTab({ history }: { history: GenerationStatsDto[] }) {
  return (
    <ResponsiveContainer width="100%" height={250}>
      <LineChart data={history} margin={{ top: 5, right: 30, left: 20, bottom: 5 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="#2a3654" />
        <XAxis dataKey="generation" stroke="#94a3b8" fontSize={12} />
        <YAxis yAxisId="left" stroke="#94a3b8" fontSize={12} />
        <YAxis yAxisId="right" orientation="right" stroke="#94a3b8" fontSize={12} />
        <Tooltip contentStyle={tooltipStyle} labelStyle={{ color: '#e2e8f0' }} />
        <Legend />
        <Line yAxisId="left" type="stepAfter" dataKey="speciesCount" name="Species" stroke="#a78bfa" strokeWidth={2} dot={false} />
        <Line yAxisId="left" type="monotone" dataKey="modulatoryEdgeCount" name="Modulatory Edges" stroke="#f472b6" strokeWidth={2} dot={false} />
        <Line yAxisId="right" type="monotone" dataKey="avgDelay" name="Avg Delay" stroke="#facc15" strokeWidth={1} dot={false} strokeDasharray="4 4" />
        {history.length > 50 && <Brush dataKey="generation" height={20} stroke="var(--color-accent)" fill="var(--color-surface)" />}
      </LineChart>
    </ResponsiveContainer>
  );
}

function SpeciesTab({ history }: { history: GenerationStatsDto[] }) {
  const allSpeciesIds = new Set<number>();
  history.forEach(h => {
    h.speciesBreakdown?.forEach(s => allSpeciesIds.add(s.speciesId));
  });
  const speciesIds = Array.from(allSpeciesIds).sort((a, b) => a - b);

  const chartData = history.map(h => {
    const entry: Record<string, number> = { generation: h.generation };
    for (const sid of speciesIds) {
      const info = h.speciesBreakdown?.find(s => s.speciesId === sid);
      entry[`sp_${sid}`] = info?.memberCount ?? 0;
    }
    return entry;
  });

  const colors = speciesIds.map(id => `hsl(${(id * 137.5) % 360}, 70%, 55%)`);

  if (speciesIds.length === 0) {
    return (
      <div className="h-[250px] flex items-center justify-center text-[var(--color-text-muted)]">
        No species data yet
      </div>
    );
  }

  return (
    <ResponsiveContainer width="100%" height={250}>
      <AreaChart data={chartData} margin={{ top: 5, right: 30, left: 20, bottom: 5 }}>
        <CartesianGrid strokeDasharray="3 3" stroke="#2a3654" />
        <XAxis dataKey="generation" stroke="#94a3b8" fontSize={12} />
        <YAxis stroke="#94a3b8" fontSize={12} />
        <Tooltip contentStyle={tooltipStyle} labelStyle={{ color: '#e2e8f0' }} />
        {speciesIds.map((sid, i) => (
          <Area key={sid} type="monotone" dataKey={`sp_${sid}`}
                stackId="species" fill={colors[i]} stroke={colors[i]}
                fillOpacity={0.6} name={`Species ${sid}`} />
        ))}
        {history.length > 50 && <Brush dataKey="generation" height={20} stroke="var(--color-accent)" fill="var(--color-surface)" />}
      </AreaChart>
    </ResponsiveContainer>
  );
}

export function FitnessChart({ history }: Props) {
  const [activeTab, setActiveTab] = useState<Tab>('fitness');

  if (history.length === 0) {
    return (
      <div className="w-full h-[250px] bg-[var(--color-surface-alt)] rounded flex items-center justify-center text-[var(--color-text-muted)]">
        No generation data yet
      </div>
    );
  }

  return (
    <div className="w-full">
      <div className="flex gap-1 mb-2">
        {tabs.map(tab => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
            className={`px-3 py-1.5 text-xs font-mono rounded transition-colors ${
              activeTab === tab.id
                ? 'bg-[var(--color-primary)] text-[var(--color-bg)] font-semibold'
                : 'bg-[var(--color-surface-alt)] text-[var(--color-text-muted)] hover:text-[var(--color-text)]'
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>
      <div style={{ height: 250, minHeight: 250 }}>
        {activeTab === 'fitness' && <FitnessTab history={history} />}
        {activeTab === 'behavior' && <BehaviorTab history={history} />}
        {activeTab === 'evolution' && <EvolutionTab history={history} />}
        {activeTab === 'species' && <SpeciesTab history={history} />}
      </div>
    </div>
  );
}
