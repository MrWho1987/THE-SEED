import { useEffect, useRef, useState, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import type { WorldFrameDto, BrainSnapshotDto, SimulationStatusDto, GenerationStatsDto, SelectedAgentDetailsDto, WorldOverrideDto } from './types';

export interface SimulationState {
  status: SimulationStatusDto | null;
  frame: WorldFrameDto | null;
  brain: BrainSnapshotDto | null;
  history: GenerationStatsDto[];
  connected: boolean;
  agentDetails: SelectedAgentDetailsDto | null;
  worldOverrides: WorldOverrideDto | null;
}

export interface SimulationControls {
  play: () => void;
  pause: () => void;
  step: () => void;
  setSpeed: (speed: number) => void;
  selectAgent: (index: number) => void;
  reset: () => void;
  applyWorldOverride: (dto: WorldOverrideDto) => void;
  clearWorldOverride: () => void;
}

export function useSignalR(): [SimulationState, SimulationControls] {
  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const [connected, setConnected] = useState(false);
  const [status, setStatus] = useState<SimulationStatusDto | null>(null);
  const [history, setHistory] = useState<GenerationStatsDto[]>([]);

  // Large, high-frequency data stored in refs to avoid React 19 dev-mode
  // DataCloneError (Performance.measure tries to serialize state)
  const frameRef = useRef<WorldFrameDto | null>(null);
  const brainRef = useRef<BrainSnapshotDto | null>(null);
  const agentDetailsRef = useRef<SelectedAgentDetailsDto | null>(null);
  const worldOverridesRef = useRef<WorldOverrideDto | null>(null);
  const [tick, setTick] = useState(0);

  useEffect(() => {
    let isMounted = true;
    
    const hubUrl = import.meta.env.DEV 
      ? 'http://localhost:5000/hub/simulation' 
      : '/hub/simulation';
    
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connection.on('WorldFrame', (data: WorldFrameDto) => {
      if (isMounted) {
        frameRef.current = data;
        setTick(t => t + 1);
      }
    });

    connection.on('BrainSnapshot', (data: BrainSnapshotDto) => {
      if (isMounted) {
        brainRef.current = data;
        setTick(t => t + 1);
      }
    });

    connection.on('BrainActivations', (activations: number[]) => {
      if (isMounted) {
        const prev = brainRef.current;
        if (prev) {
          brainRef.current = {
            ...prev,
            nodes: prev.nodes.map((node) => ({
              ...node,
              activation: activations[node.id] ?? node.activation
            }))
          };
          setTick(t => t + 1);
        }
      }
    });

    connection.on('SelectedAgentDetails', (data: SelectedAgentDetailsDto) => {
      if (isMounted) {
        agentDetailsRef.current = data;
        setTick(t => t + 1);
      }
    });

    connection.on('Status', (data: SimulationStatusDto) => {
      if (isMounted) setStatus(data);
    });

    connection.on('GenerationHistory', (data: GenerationStatsDto[]) => {
      if (isMounted) setHistory(data);
    });

    connection.on('WorldOverrides', (data: WorldOverrideDto) => {
      if (isMounted) {
        worldOverridesRef.current = data;
        setTick(t => t + 1);
      }
    });

    connection.onreconnected(() => {
      if (isMounted) setConnected(true);
    });
    
    connection.onclose(() => {
      if (isMounted) setConnected(false);
    });

    connection.start()
      .then(() => {
        if (isMounted) {
          setConnected(true);
          connectionRef.current = connection;
          console.log('✅ SignalR connected to', hubUrl);
        }
      })
      .catch(err => {
        if (isMounted) {
          console.error('SignalR connection error:', err);
        }
      });

    return () => {
      isMounted = false;
      connection.stop();
    };
  }, []);

  const play = useCallback(() => {
    connectionRef.current?.invoke('Play');
  }, []);

  const pause = useCallback(() => {
    connectionRef.current?.invoke('Pause');
  }, []);

  const step = useCallback(() => {
    connectionRef.current?.invoke('Step');
  }, []);

  const setSpeed = useCallback((speed: number) => {
    connectionRef.current?.invoke('SetSpeed', speed);
  }, []);

  const selectAgent = useCallback((index: number) => {
    connectionRef.current?.invoke('SelectAgent', index);
  }, []);

  const reset = useCallback(() => {
    connectionRef.current?.invoke('Reset');
  }, []);

  const applyWorldOverride = useCallback((dto: WorldOverrideDto) => {
    connectionRef.current?.invoke('ApplyWorldOverride', dto);
  }, []);

  const clearWorldOverride = useCallback(() => {
    connectionRef.current?.invoke('ClearWorldOverride');
  }, []);

  // tick is read here to establish the render dependency (ref changes don't trigger re-renders alone)
  void tick;
  return [
    { status, frame: frameRef.current, brain: brainRef.current, history, connected, agentDetails: agentDetailsRef.current, worldOverrides: worldOverridesRef.current },
    { play, pause, step, setSpeed, selectAgent, reset, applyWorldOverride, clearWorldOverride }
  ];
}

