import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from 'recharts';
import type { GenerationStatsDto } from '../types';

interface Props {
  history: GenerationStatsDto[];
}

export function FitnessChart({ history }: Props) {
  if (history.length === 0) {
    return (
      <div className="w-full h-[200px] bg-[var(--color-surface-alt)] rounded flex items-center justify-center text-[var(--color-text-muted)]">
        No generation data yet
      </div>
    );
  }

  return (
    <div className="w-full" style={{ height: 200, minHeight: 200 }}>
      <ResponsiveContainer width="100%" height={200}>
        <LineChart data={history} margin={{ top: 5, right: 30, left: 20, bottom: 5 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="#2a3654" />
          <XAxis 
            dataKey="generation" 
            stroke="#94a3b8"
            fontSize={12}
          />
          <YAxis 
            stroke="#94a3b8"
            fontSize={12}
          />
          <Tooltip 
            contentStyle={{
              backgroundColor: '#12182b',
              border: '1px solid #2a3654',
              borderRadius: '8px'
            }}
            labelStyle={{ color: '#e2e8f0' }}
          />
          <Legend />
          <Line 
            type="monotone" 
            dataKey="bestFitness" 
            name="Best"
            stroke="#10b981" 
            strokeWidth={2}
            dot={false}
          />
          <Line 
            type="monotone" 
            dataKey="meanFitness" 
            name="Mean"
            stroke="#06b6d4" 
            strokeWidth={2}
            dot={false}
          />
          <Line 
            type="monotone" 
            dataKey="worstFitness" 
            name="Worst"
            stroke="#ef4444" 
            strokeWidth={1}
            dot={false}
            strokeDasharray="5 5"
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}

