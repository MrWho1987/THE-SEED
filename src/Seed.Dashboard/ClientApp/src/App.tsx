import { useState, useCallback } from 'react';
import { useSignalR } from './useSignalR';
import { WorldView } from './components/WorldView';
import { BrainView } from './components/BrainView';
import { ControlPanel } from './components/ControlPanel';
import { FitnessChart } from './components/FitnessChart';
import { ReplayPlayer } from './components/ReplayPlayer';
import type { WorldFrameDto } from './types';
import './App.css';

function App() {
  const [state, controls] = useSignalR();
  const [replayFrames, setReplayFrames] = useState<WorldFrameDto[]>([]);
  const [replayMode, setReplayMode] = useState(false);
  const [replayFrame, setReplayFrame] = useState<WorldFrameDto | null>(null);
  const [isRecording, setIsRecording] = useState(false);
  const [selectedAgentId, setSelectedAgentId] = useState(0);

  const handleSelectAgent = useCallback((id: number) => {
    setSelectedAgentId(id);
    controls.selectAgent(id);
  }, [controls]);

  const handleStartRecording = useCallback(async () => {
    await fetch('/api/sim/start-recording', { method: 'POST' });
    setIsRecording(true);
  }, []);

  const handleStopRecording = useCallback(async () => {
    const response = await fetch('/api/sim/stop-recording', { method: 'POST' });
    const frames = await response.json();
    setReplayFrames(frames);
    setIsRecording(false);
    setReplayMode(true);
    controls.pause();
  }, [controls]);

  const handleCloseReplay = useCallback(() => {
    setReplayMode(false);
    setReplayFrame(null);
  }, []);

  const displayFrame = replayMode ? replayFrame : state.frame;

  return (
    <div className="min-h-screen bg-[var(--color-bg)] text-[var(--color-text)]">
      {/* Header */}
      <header className="border-b border-[var(--color-border)] px-4 py-3 flex items-center justify-between">
        <div className="flex items-center gap-4">
          <h1 className="text-xl font-bold text-[var(--color-accent)]">
            🌱 Seed Dashboard
          </h1>
          <span className={`px-2 py-1 text-xs rounded ${
            state.connected 
              ? 'bg-[var(--color-success)]/20 text-[var(--color-success)]' 
              : 'bg-[var(--color-danger)]/20 text-[var(--color-danger)]'
          }`}>
            {state.connected ? 'Connected' : 'Disconnected'}
          </span>
          {replayMode && (
            <span className="px-2 py-1 text-xs rounded bg-[var(--color-accent)]/20 text-[var(--color-accent)]">
              Replay Mode
            </span>
          )}
        </div>
        <div className="flex items-center gap-4">
          {/* Recording Controls */}
          <div className="flex gap-2">
            {!isRecording ? (
              <button
                onClick={handleStartRecording}
                disabled={replayMode}
                className="px-3 py-1 bg-[var(--color-danger)] hover:bg-[var(--color-danger)]/80 
                           disabled:opacity-50 rounded text-sm font-semibold"
              >
                ⏺ Record
              </button>
            ) : (
              <button
                onClick={handleStopRecording}
                className="px-3 py-1 bg-[var(--color-danger)] animate-pulse rounded text-sm font-semibold"
              >
                ⏹ Stop ({replayFrames.length})
              </button>
            )}
          </div>
          
          {state.status && !replayMode && (
            <div className="flex gap-6 text-sm text-[var(--color-text-muted)]">
              <span>Gen: <strong className="text-[var(--color-text)]">{state.status.currentGeneration}</strong></span>
              <span>Tick: <strong className="text-[var(--color-text)]">{state.status.currentTick}</strong></span>
              <span>Round: <strong className="text-[var(--color-text)]">{state.status.currentRound + 1}</strong></span>
              <span>Alive: <strong className="text-[var(--color-text)]">{state.status.aliveCount}/{state.status.populationSize}</strong></span>
              <span>Species: <strong className="text-[var(--color-text)]">{state.status.speciesCount}</strong></span>
            </div>
          )}
        </div>
      </header>

      {/* Control Panel */}
      {!replayMode && (
        <ControlPanel 
          status={state.status} 
          controls={controls} 
        />
      )}

      {/* Replay Player */}
      {replayMode && (
        <ReplayPlayer 
          frames={replayFrames}
          onFrameChange={setReplayFrame}
          onClose={handleCloseReplay}
        />
      )}

      {/* Main Content */}
      <main className="p-4 grid grid-cols-1 lg:grid-cols-2 gap-4">
        {/* World View */}
        <div className="bg-[var(--color-surface)] rounded-lg border border-[var(--color-border)] p-4">
          <h2 className="text-lg font-semibold mb-3 text-[var(--color-accent)]">World View</h2>
          <WorldView 
            frame={displayFrame} 
            onSelectAgent={handleSelectAgent}
            selectedAgentId={selectedAgentId}
          />
        </div>

        {/* Brain View */}
        <div className="bg-[var(--color-surface)] rounded-lg border border-[var(--color-border)] p-4">
          <h2 className="text-lg font-semibold mb-3 text-[var(--color-accent)]">Brain Graph</h2>
          <BrainView brain={state.brain} />
        </div>
      </main>

      {/* Fitness Chart */}
      <section className="px-4 pb-4">
        <div className="bg-[var(--color-surface)] rounded-lg border border-[var(--color-border)] p-4">
          <h2 className="text-lg font-semibold mb-3 text-[var(--color-accent)]">Fitness History</h2>
          <FitnessChart history={state.history} />
        </div>
      </section>
    </div>
  );
}

export default App;
