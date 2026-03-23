import { useEffect, useRef, useState, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import type { WorldFrameDto, BrainSnapshotDto, SimulationStatusDto, GenerationStatsDto } from './types';

export interface SimulationState {
  status: SimulationStatusDto | null;
  frame: WorldFrameDto | null;
  brain: BrainSnapshotDto | null;
  history: GenerationStatsDto[];
  connected: boolean;
}

export interface SimulationControls {
  play: () => void;
  pause: () => void;
  step: () => void;
  setSpeed: (speed: number) => void;
  selectAgent: (index: number) => void;
  reset: () => void;
}

export function useSignalR(): [SimulationState, SimulationControls] {
  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const [connected, setConnected] = useState(false);
  const [status, setStatus] = useState<SimulationStatusDto | null>(null);
  const [frame, setFrame] = useState<WorldFrameDto | null>(null);
  const [brain, setBrain] = useState<BrainSnapshotDto | null>(null);
  const [history, setHistory] = useState<GenerationStatsDto[]>([]);

  useEffect(() => {
    let isMounted = true;
    
    // Connect directly to backend (avoid proxy issues with WebSocket)
    const hubUrl = import.meta.env.DEV 
      ? 'http://localhost:5000/hub/simulation' 
      : '/hub/simulation';
    
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connection.on('WorldFrame', (data: WorldFrameDto) => {
      if (isMounted) setFrame(data);
    });

    connection.on('BrainSnapshot', (data: BrainSnapshotDto) => {
      if (isMounted) setBrain(data);
    });

    // Lightweight activation-only updates (don't recreate brain structure)
    connection.on('BrainActivations', (activations: number[]) => {
      if (isMounted) {
        setBrain(prev => {
          if (!prev) return prev;
          // Update activations in-place without creating new node objects
          const updatedNodes = prev.nodes.map((node, i) => ({
            ...node,
            activation: activations[node.id] ?? node.activation
          }));
          return { ...prev, nodes: updatedNodes };
        });
      }
    });

    connection.on('Status', (data: SimulationStatusDto) => {
      if (isMounted) setStatus(data);
    });

    connection.on('GenerationHistory', (data: GenerationStatsDto[]) => {
      if (isMounted) setHistory(data);
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

  return [
    { status, frame, brain, history, connected },
    { play, pause, step, setSpeed, selectAgent, reset }
  ];
}

