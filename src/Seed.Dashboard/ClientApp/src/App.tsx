import { useState, useCallback } from 'react';
import { useSignalR } from './useSignalR';
import { WorldView } from './components/WorldView';
import { BrainView } from './components/BrainView';
import { ControlPanel } from './components/ControlPanel';
import { FitnessChart } from './components/FitnessChart';
import { ReplayPlayer } from './components/ReplayPlayer';
import { EnvironmentBar } from './components/EnvironmentBar';
import { AgentInspector } from './components/AgentInspector';
import { WorldControls } from './components/WorldControls';
import type { WorldFrameDto } from './types';
import './App.css';

function App() {
  const [state, controls] = useSignalR();
  const [replayFrames, setReplayFrames] = useState<WorldFrameDto[]>([]);
  const [replayMode, setReplayMode] = useState(false);
  const [replayFrame, setReplayFrame] = useState<WorldFrameDto | null>(null);
  const [isRecording, setIsRecording] = useState(false);
  const [selectedAgentId, setSelectedAgentId] = useState(0);
  const [brainCollapsed, setBrainCollapsed] = useState(false);

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
  const selectedAgent = displayFrame?.agents.find(a => a.id === selectedAgentId) ?? null;

  return (
    <div className="min-h-screen bg-[var(--color-bg)] text-[var(--color-text)]">
      {/* Header */}
      <header className="border-b border-[var(--color-border)] px-4 py-2 flex items-center justify-between">
        <div className="flex items-center gap-3">
          <h1 className="text-lg font-bold text-[var(--color-accent)]">
            The Seed
          </h1>
          <span className={`px-2 py-0.5 text-[10px] rounded font-mono ${
            state.connected 
              ? 'bg-[var(--color-success)]/20 text-[var(--color-success)]' 
              : 'bg-[var(--color-danger)]/20 text-[var(--color-danger)]'
          }`}>
            {state.connected ? 'ONLINE' : 'OFFLINE'}
          </span>
          {replayMode && (
            <span className="px-2 py-0.5 text-[10px] rounded font-mono bg-[var(--color-accent)]/20 text-[var(--color-accent)]">
              REPLAY
            </span>
          )}
        </div>
        <div className="flex items-center gap-4">
          <div className="flex gap-2">
            {!isRecording ? (
              <button
                onClick={handleStartRecording}
                disabled={replayMode}
                className="px-2.5 py-1 bg-[var(--color-danger)] hover:bg-[var(--color-danger)]/80 
                           disabled:opacity-50 rounded text-xs font-semibold"
              >
                REC
              </button>
            ) : (
              <button
                onClick={handleStopRecording}
                className="px-2.5 py-1 bg-[var(--color-danger)] animate-pulse rounded text-xs font-semibold"
              >
                STOP ({replayFrames.length})
              </button>
            )}
          </div>
          
          {/* Gen/Rnd/Species moved to EnvironmentBar */}
        </div>
      </header>

      {/* Control Panel */}
      {!replayMode && (
        <ControlPanel 
          status={state.status} 
          controls={controls} 
        />
      )}

      {/* World Controls */}
      {!replayMode && (
        <WorldControls status={state.status} controls={controls} initialOverrides={state.worldOverrides} />
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
      <main className="p-3 grid grid-cols-1 lg:grid-cols-[1fr_380px] gap-3">
        {/* Left Column */}
        <div className="flex flex-col gap-3">
          <div className="bg-[var(--color-surface)] rounded-lg border border-[var(--color-border)] p-3">
            <WorldView 
              frame={displayFrame} 
              onSelectAgent={handleSelectAgent}
              selectedAgentId={selectedAgentId}
              speed={state.status?.speed ?? 1}
            />
          </div>
          <EnvironmentBar frame={displayFrame} status={state.status} />
        </div>

        {/* Right Column */}
        <div className="flex flex-col gap-3">
          <div className="bg-[var(--color-surface)] rounded-lg border border-[var(--color-border)]">
            <div className="px-3 py-2 border-b border-[var(--color-border)]">
              <h2 className="text-xs font-semibold uppercase tracking-wider text-[var(--color-text-muted)]">Agent Inspector</h2>
            </div>
            <AgentInspector agent={selectedAgent} speciesId={selectedAgent?.speciesId} details={state.agentDetails} />
          </div>

          <div className="bg-[var(--color-surface)] rounded-lg border border-[var(--color-border)] flex-1 min-h-0">
            <button
              onClick={() => setBrainCollapsed(!brainCollapsed)}
              className="w-full px-3 py-2 border-b border-[var(--color-border)] flex items-center justify-between hover:bg-[var(--color-surface-alt)] transition-colors"
            >
              <h2 className="text-xs font-semibold uppercase tracking-wider text-[var(--color-text-muted)]">Brain Graph</h2>
              <span className="text-[var(--color-text-muted)] text-xs">{brainCollapsed ? '▸' : '▾'}</span>
            </button>
            {!brainCollapsed && (
              <div className="p-3">
                <BrainView brain={state.brain} />
              </div>
            )}
          </div>
        </div>
      </main>

      {/* Charts Section */}
      <section className="px-3 pb-3">
        <div className="bg-[var(--color-surface)] rounded-lg border border-[var(--color-border)] p-3">
          <FitnessChart
            history={state.history}
            terrariumHistory={state.terrariumHistory}
            terrariumMode={state.status?.terrariumMode ?? false}
          />
        </div>
      </section>
    </div>
  );
}

export default App;
