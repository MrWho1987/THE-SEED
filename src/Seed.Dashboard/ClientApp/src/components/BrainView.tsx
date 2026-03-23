import { useRef, useEffect, useCallback, useMemo, useState } from 'react';
import ForceGraph2D from 'react-force-graph-2d';
import type { BrainSnapshotDto } from '../types';

interface Props {
  brain: BrainSnapshotDto | null;
}

interface GraphNode {
  id: number;
  type: string;
  activation: number;
  label: string | null;
  x?: number;
  y?: number;
  fx?: number;
  fy?: number;
}

interface GraphLink {
  source: number | GraphNode;
  target: number | GraphNode;
  weight: number;
  linkType: string;
  delay: number;
}

export function BrainView({ brain }: Props) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const graphRef = useRef<any>(null);
  const [isLayoutFrozen, setIsLayoutFrozen] = useState(false);
  const prevAgentIdRef = useRef<number | null>(null);
  
  // Track node positions to preserve layout
  const nodePositionsRef = useRef<Map<number, {x: number, y: number}>>(new Map());

  // Detect agent change to reset layout
  const agentId = brain?.agentId ?? -1;
  
  useEffect(() => {
    if (agentId !== prevAgentIdRef.current) {
      prevAgentIdRef.current = agentId;
      nodePositionsRef.current.clear();
      setIsLayoutFrozen(false);
    }
  }, [agentId]);

  // Build graph data with position preservation
  const graphData = useMemo(() => {
    if (!brain) return { nodes: [] as GraphNode[], links: [] as GraphLink[] };
    
    const nodes: GraphNode[] = brain.nodes.map(n => {
      const savedPos = nodePositionsRef.current.get(n.id);
      const baseX = n.x * 30;
      const baseY = n.y * 30;
      
      return {
        id: n.id,
        x: savedPos?.x ?? baseX,
        y: savedPos?.y ?? baseY,
        fx: isLayoutFrozen ? (savedPos?.x ?? baseX) : undefined,
        fy: isLayoutFrozen ? (savedPos?.y ?? baseY) : undefined,
        type: n.type,
        activation: n.activation,
        label: n.label
      };
    });
    
    const links: GraphLink[] = brain.edges.map(e => ({
      source: e.from,
      target: e.to,
      weight: e.weight,
      linkType: e.type,
      delay: e.delay ?? 0
    }));
    
    return { nodes, links };
  }, [brain, isLayoutFrozen]);

  // Freeze layout after simulation settles
  const handleEngineStop = useCallback(() => {
    if (!isLayoutFrozen) {
      // Save current positions
      graphData.nodes.forEach((node) => {
        if (node.x !== undefined && node.y !== undefined) {
          nodePositionsRef.current.set(node.id, { x: node.x, y: node.y });
        }
      });
      setIsLayoutFrozen(true);
      
      // Zoom to fit
      setTimeout(() => {
        graphRef.current?.zoomToFit?.(400);
      }, 100);
    }
  }, [isLayoutFrozen, graphData.nodes]);

  const nodeColor = useCallback((node: GraphNode) => {
    const activation = Math.abs(node.activation || 0);
    
    switch (node.type) {
      case 'Input':
        return `rgba(6, 182, 212, ${0.3 + activation * 0.7})`;
      case 'Output':
        return `rgba(16, 185, 129, ${0.3 + activation * 0.7})`;
      case 'Hidden':
      default:
        return `rgba(148, 163, 184, ${0.3 + activation * 0.7})`;
    }
  }, []);

  const linkColor = useCallback((link: GraphLink) => {
    const weight = typeof link.weight === 'number' ? link.weight : 0;
    const absWeight = Math.abs(weight);
    
    if (absWeight < 0.1) {
      return 'rgba(50, 50, 50, 0.05)';
    }
    
    const normalizedWeight = Math.min(absWeight, 2.0) / 2.0;
    const opacity = 0.2 + normalizedWeight * 0.8;
    
    if (link.linkType === 'Modulatory') {
      return `rgba(168, 85, 247, ${opacity})`; // Purple for modulatory
    } else if (weight > 0) {
      return `rgba(34, 211, 238, ${opacity})`;  // Cyan for positive
    } else {
      return `rgba(239, 68, 68, ${opacity})`;   // Red for negative
    }
  }, []);

  const linkLineDash = useCallback((link: GraphLink) => {
    return link.delay > 0 ? [4, 2] : null;
  }, []);

  const linkWidth = useCallback((link: GraphLink) => {
    const weight = typeof link.weight === 'number' ? link.weight : 0;
    const absWeight = Math.abs(weight);
    
    // Very weak edges are nearly invisible
    if (absWeight < 0.1) {
      return 0.2;
    }
    
    // Scale width by magnitude (0.5 to 4 range)
    return Math.min(absWeight * 2, 4) + 0.5;
  }, []);

  const nodeLabel = useCallback((node: GraphNode) => {
    return node.label || `${node.type} ${node.id}`;
  }, []);

  if (!brain) {
    return (
      <div className="w-full h-[400px] bg-[var(--color-surface-alt)] rounded flex items-center justify-center text-[var(--color-text-muted)]">
        Select an agent to view brain
      </div>
    );
  }

  return (
    <div className="w-full h-[400px] bg-[var(--color-surface-alt)] rounded overflow-hidden">
      <ForceGraph2D
        ref={graphRef}
        graphData={graphData}
        nodeColor={nodeColor}
        nodeLabel={nodeLabel}
        nodeRelSize={6}
        linkColor={linkColor}
        linkWidth={linkWidth}
        linkLineDash={linkLineDash}
        linkDirectionalArrowLength={4}
        linkDirectionalArrowRelPos={1}
        backgroundColor="#1a2340"
        cooldownTicks={isLayoutFrozen ? 0 : 100}
        cooldownTime={isLayoutFrozen ? 0 : 3000}
        onEngineStop={handleEngineStop}
        enableNodeDrag={true}
        enableZoomInteraction={true}
        enablePanInteraction={true}
      />
    </div>
  );
}
