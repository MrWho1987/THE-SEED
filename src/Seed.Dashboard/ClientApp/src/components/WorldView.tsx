import { useRef, useEffect, useCallback } from 'react';
import type { WorldFrameDto } from '../types';

interface Props {
  frame: WorldFrameDto | null;
  onSelectAgent: (index: number) => void;
  selectedAgentId?: number;
}

const CANVAS_SIZE = 500;

export function WorldView({ frame, onSelectAgent, selectedAgentId }: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const selectedAgentRef = useRef<number>(selectedAgentId ?? -1);
  selectedAgentRef.current = selectedAgentId ?? -1;

  const draw = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas || !frame) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const worldSize = Math.max(frame.worldWidth, frame.worldHeight);
    const scale = CANVAS_SIZE / worldSize;
    
    // Clear with dark background
    ctx.fillStyle = '#0a0e17';
    ctx.fillRect(0, 0, CANVAS_SIZE, CANVAS_SIZE);

    // Draw grid
    ctx.strokeStyle = '#1a2340';
    ctx.lineWidth = 0.5;
    const gridSize = 8;
    for (let i = 0; i <= worldSize; i += gridSize) {
      // Vertical lines
      ctx.beginPath();
      ctx.moveTo(i * scale, 0);
      ctx.lineTo(i * scale, CANVAS_SIZE);
      ctx.stroke();
      // Horizontal lines
      ctx.beginPath();
      ctx.moveTo(0, i * scale);
      ctx.lineTo(CANVAS_SIZE, i * scale);
      ctx.stroke();
    }

    // Draw obstacles (dark gray rectangles)
    ctx.fillStyle = '#4b5563';
    ctx.strokeStyle = '#6b7280';
    ctx.lineWidth = 1;
    for (const obs of frame.obstacles) {
      const rx = obs.x * scale;
      const ry = obs.y * scale;
      const rw = obs.width * scale;
      const rh = obs.height * scale;
      ctx.fillRect(rx, ry, rw, rh);
      ctx.strokeRect(rx, ry, rw, rh);
    }

    // Draw hazards (red semi-transparent rectangles)
    ctx.fillStyle = 'rgba(239, 68, 68, 0.4)';
    ctx.strokeStyle = '#ef4444';
    ctx.lineWidth = 1;
    for (const haz of frame.hazards) {
      const rx = haz.x * scale;
      const ry = haz.y * scale;
      const rw = haz.width * scale;
      const rh = haz.height * scale;
      ctx.fillRect(rx, ry, rw, rh);
      ctx.strokeRect(rx, ry, rw, rh);
    }

    // Draw food (green glowing dots, modulated by energy oscillation)
    const mult = frame.foodEnergyMultiplier ?? 1;
    const glowAlpha = 0.4 + 0.4 * Math.min(mult, 1.4);
    const coreGreen = Math.round(100 + 85 * Math.min(mult, 1.4));
    for (const food of frame.food) {
      const fx = food.x * scale;
      const fy = food.y * scale;
      
      const gradient = ctx.createRadialGradient(fx, fy, 0, fx, fy, 8);
      gradient.addColorStop(0, `rgba(16, ${coreGreen}, 129, ${glowAlpha.toFixed(2)})`);
      gradient.addColorStop(1, 'rgba(16, 185, 129, 0)');
      ctx.fillStyle = gradient;
      ctx.beginPath();
      ctx.arc(fx, fy, 8, 0, Math.PI * 2);
      ctx.fill();
      
      ctx.fillStyle = `rgb(16, ${coreGreen}, 129)`;
      ctx.beginPath();
      ctx.arc(fx, fy, 3, 0, Math.PI * 2);
      ctx.fill();
    }

    // Draw agents
    for (const agent of frame.agents) {
      const ax = agent.x * scale;
      const ay = agent.y * scale;
      const radius = 6;
      const isSelected = agent.id === selectedAgentRef.current;

      // Species-based hue using golden angle for maximum separation
      const speciesHue = (agent.speciesId * 137.5) % 360;
      const speciesColor = `hsl(${speciesHue}, 70%, 55%)`;

      if (!agent.alive) {
        ctx.globalAlpha = 0.2;
        ctx.fillStyle = '#6b7280';
        ctx.beginPath();
        ctx.arc(ax, ay, radius, 0, Math.PI * 2);
        ctx.fill();
        ctx.globalAlpha = 1.0;
        continue;
      }

      // Glow (brighter for selected)
      const glowRadius = isSelected ? radius * 3 : radius * 1.8;
      const glowAlpha = isSelected ? 0.5 : 0.2;
      const glowGradient = ctx.createRadialGradient(ax, ay, 0, ax, ay, glowRadius);
      glowGradient.addColorStop(0, speciesColor.replace('55%)', `${glowAlpha * 100}%)`).replace('hsl', 'hsla').replace(')', `, ${glowAlpha})`));
      glowGradient.addColorStop(1, 'rgba(0,0,0,0)');
      ctx.fillStyle = glowGradient;
      ctx.beginPath();
      ctx.arc(ax, ay, glowRadius, 0, Math.PI * 2);
      ctx.fill();

      // Body
      ctx.fillStyle = speciesColor;
      ctx.beginPath();
      ctx.arc(ax, ay, radius, 0, Math.PI * 2);
      ctx.fill();

      // Border
      ctx.strokeStyle = isSelected ? '#ffffff' : speciesColor;
      ctx.lineWidth = isSelected ? 2.5 : 1;
      ctx.stroke();

      // Heading arrow
      const headingX = Math.cos(agent.heading);
      const headingY = Math.sin(agent.heading);
      ctx.strokeStyle = '#ffffff';
      ctx.lineWidth = 1.5;
      ctx.beginPath();
      ctx.moveTo(ax, ay);
      ctx.lineTo(ax + headingX * radius * 1.8, ay + headingY * radius * 1.8);
      ctx.stroke();

      // Signal ring
      const sigMag = Math.abs(agent.signal0) + Math.abs(agent.signal1);
      if (sigMag > 0.05) {
        const ringRadius = radius + 4;
        const sigHue = ((agent.signal0 + 1) / 2) * 360;
        const sigSat = Math.min(Math.abs(agent.signal1) * 100, 100);
        ctx.strokeStyle = `hsla(${sigHue}, ${sigSat}%, 60%, ${Math.min(sigMag, 1)})`;
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        ctx.arc(ax, ay, ringRadius, 0, Math.PI * 2);
        ctx.stroke();
      }

      // Interaction rings
      if (agent.attackReceived > 0.001) {
        ctx.strokeStyle = `rgba(255, 56, 100, ${Math.min(agent.attackReceived * 20, 0.9)})`;
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.arc(ax, ay, radius + 6, 0, Math.PI * 2);
        ctx.stroke();
      }

      if (agent.shareReceived > 0.001) {
        ctx.strokeStyle = `rgba(0, 246, 161, ${Math.min(agent.shareReceived * 20, 0.9)})`;
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.arc(ax, ay, radius + 8, 0, Math.PI * 2);
        ctx.stroke();
      }

      // Energy bar (only for selected agent)
      if (isSelected) {
        const barWidth = 20;
        const barHeight = 3;
        const barX = ax - barWidth / 2;
        const barY = ay - radius - 8;
        ctx.fillStyle = '#374151';
        ctx.fillRect(barX, barY, barWidth, barHeight);
        const energyHue = Math.floor(agent.energy * 120);
        ctx.fillStyle = `hsl(${energyHue}, 70%, 50%)`;
        ctx.fillRect(barX, barY, barWidth * Math.max(0, agent.energy), barHeight);
      }
    }

    // World border
    ctx.strokeStyle = '#06b6d4';
    ctx.lineWidth = 2;
    ctx.strokeRect(1, 1, frame.worldWidth * scale - 2, frame.worldHeight * scale - 2);

  }, [frame]);

  useEffect(() => {
    draw();
  }, [draw]);

  const handleClick = useCallback((e: React.MouseEvent<HTMLCanvasElement>) => {
    if (!frame) return;
    
    const canvas = canvasRef.current;
    if (!canvas) return;

    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;
    const scale = CANVAS_SIZE / Math.max(frame.worldWidth, frame.worldHeight);

    // Check if clicked on an agent
    for (const agent of frame.agents) {
      const ax = agent.x * scale;
      const ay = agent.y * scale;
      const dist = Math.sqrt((x - ax) ** 2 + (y - ay) ** 2);
      if (dist < 15) {
        onSelectAgent(agent.id);
        return;
      }
    }
  }, [frame, onSelectAgent]);

  if (!frame) {
    return (
      <div className="w-[500px] h-[500px] bg-[var(--color-surface-alt)] rounded flex items-center justify-center text-[var(--color-text-muted)]">
        Waiting for simulation data...
      </div>
    );
  }

  return (
    <canvas
      ref={canvasRef}
      width={CANVAS_SIZE}
      height={CANVAS_SIZE}
      onClick={handleClick}
      className="rounded cursor-crosshair border border-[var(--color-border)]"
    />
  );
}
