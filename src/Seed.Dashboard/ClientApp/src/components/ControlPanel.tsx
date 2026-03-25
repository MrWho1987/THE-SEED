import { useState } from 'react';
import type { SimulationStatusDto } from '../types';
import type { SimulationControls } from '../useSignalR';

interface Props {
  status: SimulationStatusDto | null;
  controls: SimulationControls;
}

export function ControlPanel({ status, controls }: Props) {
  const [speed, setSpeed] = useState(1);

  const handleSpeedChange = (newSpeed: number) => {
    setSpeed(newSpeed);
    controls.setSpeed(newSpeed);
  };

  return (
    <div className="border-b border-[var(--color-border)] px-4 py-3 flex items-center gap-4 flex-wrap">
      {/* Play/Pause/Step */}
      <div className="flex gap-2">
        <button
          onClick={controls.play}
          disabled={status?.isRunning && !status?.isPaused}
          className="px-4 py-2 bg-[var(--color-success)] hover:bg-[var(--color-success)]/80 
                     disabled:opacity-50 disabled:cursor-not-allowed rounded font-semibold
                     transition-colors"
        >
          ▶ Play
        </button>
        <button
          onClick={controls.pause}
          disabled={status?.isPaused}
          className="px-4 py-2 bg-[var(--color-warning)] hover:bg-[var(--color-warning)]/80 
                     disabled:opacity-50 disabled:cursor-not-allowed rounded font-semibold
                     transition-colors"
        >
          ⏸ Pause
        </button>
        <button
          onClick={controls.step}
          disabled={!status?.isPaused}
          className="px-4 py-2 bg-[var(--color-accent)] hover:bg-[var(--color-accent)]/80 
                     disabled:opacity-50 disabled:cursor-not-allowed rounded font-semibold
                     transition-colors"
        >
          ⏭ Step
        </button>
        <button
          onClick={controls.reset}
          className="px-4 py-2 bg-[var(--color-danger)] hover:bg-[var(--color-danger)]/80 
                     rounded font-semibold transition-colors"
        >
          ↺ Reset
        </button>
      </div>

      {/* Speed Control */}
      <div className="flex items-center gap-3">
        <span className="text-sm text-[var(--color-text-muted)]">Speed:</span>
        <input
          type="range"
          min="0.1"
          max="10"
          step="0.1"
          value={speed}
          onChange={(e) => handleSpeedChange(parseFloat(e.target.value))}
          className="w-32 accent-[var(--color-accent)]"
        />
        <span className="text-sm font-mono w-12">{speed.toFixed(1)}x</span>
      </div>

      {/* Speed Presets */}
      <div className="flex gap-1">
        {([
          { value: 0.1, label: '0.1x', name: 'Observe' },
          { value: 0.25, label: '0.25x', name: 'Slow' },
          { value: 0.5, label: '0.5x' },
          { value: 1, label: '1x' },
          { value: 2, label: '2x' },
          { value: 5, label: '5x' },
          { value: 10, label: '10x' },
        ] as const).map((s) => (
          <button
            key={s.value}
            onClick={() => handleSpeedChange(s.value)}
            title={'name' in s ? s.name : undefined}
            className={`px-2 py-1 text-xs rounded transition-colors ${
              speed === s.value
                ? 'bg-[var(--color-accent)] text-black'
                : 'bg-[var(--color-surface-alt)] hover:bg-[var(--color-border)]'
            }`}
          >
            {s.label}
          </button>
        ))}
      </div>

      {/* Status Indicator */}
      <div className="ml-auto flex items-center gap-2">
        <span className={`w-3 h-3 rounded-full ${
          status?.isPaused 
            ? 'bg-[var(--color-warning)]' 
            : 'bg-[var(--color-success)] animate-pulse'
        }`} />
        <span className="text-sm text-[var(--color-text-muted)]">
          {status?.isPaused ? 'Paused' : 'Running'}
        </span>
      </div>
    </div>
  );
}


