import { useRef, useEffect, useCallback, useState } from 'react';
import type { WorldFrameDto } from '../types';

interface Props {
  frame: WorldFrameDto | null;
  onSelectAgent: (index: number) => void;
  selectedAgentId?: number;
  speed?: number;
}

function drawWedge(
  ctx: CanvasRenderingContext2D,
  x: number, y: number,
  heading: number, radius: number, color: string
) {
  const tipX = x + Math.cos(heading) * radius * 1.6;
  const tipY = y + Math.sin(heading) * radius * 1.6;
  const leftX = x + Math.cos(heading + 2.4) * radius;
  const leftY = y + Math.sin(heading + 2.4) * radius;
  const rightX = x + Math.cos(heading - 2.4) * radius;
  const rightY = y + Math.sin(heading - 2.4) * radius;
  const tailX = x - Math.cos(heading) * radius * 0.4;
  const tailY = y - Math.sin(heading) * radius * 0.4;

  ctx.beginPath();
  ctx.moveTo(tipX, tipY);
  ctx.quadraticCurveTo(
    x + Math.cos(heading + 1.2) * radius * 1.1,
    y + Math.sin(heading + 1.2) * radius * 1.1,
    leftX, leftY
  );
  ctx.lineTo(tailX, tailY);
  ctx.lineTo(rightX, rightY);
  ctx.quadraticCurveTo(
    x + Math.cos(heading - 1.2) * radius * 1.1,
    y + Math.sin(heading - 1.2) * radius * 1.1,
    tipX, tipY
  );
  ctx.closePath();
  ctx.fillStyle = color;
  ctx.fill();
}

export function WorldView({ frame, onSelectAgent, selectedAgentId, speed }: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const selectedAgentRef = useRef<number>(selectedAgentId ?? -1);
  selectedAgentRef.current = selectedAgentId ?? -1;
  const [canvasSize, setCanvasSize] = useState(500);

  const interactionDecay = useRef<Map<number, { share: number; attack: number }>>(new Map());
  const trailRef = useRef<{ x: number; y: number }[]>([]);
  const prevSelectedRef = useRef<number>(-1);
  const prevGenRound = useRef({ gen: -1, round: -1 });
  const MAX_TRAIL = 60;

  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;

    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        const w = entry.contentRect.width;
        if (w > 0) setCanvasSize(Math.floor(w));
      }
    });
    observer.observe(container);
    setCanvasSize(Math.floor(container.clientWidth));
    return () => observer.disconnect();
  }, []);

  const draw = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas || !frame) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const CS = canvasSize;
    const worldSize = Math.max(frame.worldWidth, frame.worldHeight);
    const scale = CS / worldSize;
    const light = frame.lightLevel ?? 1;
    const currentSpeed = speed ?? 1;

    // --- Buffer clearing on gen/round/selection change ---
    if (frame.generation !== prevGenRound.current.gen ||
        frame.worldIndex !== prevGenRound.current.round) {
      trailRef.current = [];
      interactionDecay.current.clear();
      prevGenRound.current = { gen: frame.generation, round: frame.worldIndex };
    }
    if (selectedAgentRef.current !== prevSelectedRef.current) {
      trailRef.current = [];
      prevSelectedRef.current = selectedAgentRef.current;
    }

    // 1. Background
    ctx.fillStyle = '#0a0e17';
    ctx.fillRect(0, 0, CS, CS);

    const gridAlpha = 0.15 + 0.35 * light;
    ctx.strokeStyle = `rgba(26, 35, 64, ${gridAlpha})`;
    ctx.lineWidth = 0.5;
    const gridSize = 8;
    for (let i = 0; i <= worldSize; i += gridSize) {
      ctx.beginPath();
      ctx.moveTo(i * scale, 0);
      ctx.lineTo(i * scale, CS);
      ctx.stroke();
      ctx.beginPath();
      ctx.moveTo(0, i * scale);
      ctx.lineTo(CS, i * scale);
      ctx.stroke();
    }

    // 2. Obstacles
    ctx.fillStyle = '#4b5563';
    ctx.strokeStyle = '#6b7280';
    ctx.lineWidth = 1;
    for (const obs of frame.obstacles) {
      ctx.fillRect(obs.x * scale, obs.y * scale, obs.width * scale, obs.height * scale);
      ctx.strokeRect(obs.x * scale, obs.y * scale, obs.width * scale, obs.height * scale);
    }

    // 2b. Hazards
    ctx.fillStyle = 'rgba(239, 68, 68, 0.4)';
    ctx.strokeStyle = '#ef4444';
    ctx.lineWidth = 1;
    for (const haz of frame.hazards) {
      ctx.fillRect(haz.x * scale, haz.y * scale, haz.width * scale, haz.height * scale);
      ctx.strokeRect(haz.x * scale, haz.y * scale, haz.width * scale, haz.height * scale);
    }

    // 3. Food
    const mult = frame.foodEnergyMultiplier ?? 1;
    for (const food of frame.food) {
      const fx = food.x * scale;
      const fy = food.y * scale;
      const foodRadius = 2 + (food.value / 0.3) * 2;
      const glowR = foodRadius + 5;
      const glowAlpha = 0.3 + 0.4 * Math.min(mult, 1.4);

      if (food.isCorpse) {
        const grad = ctx.createRadialGradient(fx, fy, 0, fx, fy, glowR);
        grad.addColorStop(0, `rgba(245, 158, 11, ${glowAlpha.toFixed(2)})`);
        grad.addColorStop(1, 'rgba(245, 158, 11, 0)');
        ctx.fillStyle = grad;
        ctx.beginPath();
        ctx.arc(fx, fy, glowR, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = 'rgb(245, 158, 11)';
        ctx.beginPath();
        ctx.arc(fx, fy, foodRadius, 0, Math.PI * 2);
        ctx.fill();
      } else {
        const coreGreen = Math.round(100 + 85 * Math.min(food.value / 0.25, 1.4));
        const grad = ctx.createRadialGradient(fx, fy, 0, fx, fy, glowR);
        grad.addColorStop(0, `rgba(16, ${coreGreen}, 129, ${glowAlpha.toFixed(2)})`);
        grad.addColorStop(1, 'rgba(16, 185, 129, 0)');
        ctx.fillStyle = grad;
        ctx.beginPath();
        ctx.arc(fx, fy, glowR, 0, Math.PI * 2);
        ctx.fill();
        ctx.fillStyle = `rgb(16, ${coreGreen}, 129)`;
        ctx.beginPath();
        ctx.arc(fx, fy, foodRadius, 0, Math.PI * 2);
        ctx.fill();
      }
    }

    // 4. Selected agent trail (behind agents)
    if (trailRef.current.length > 1) {
      ctx.lineWidth = 1.5;
      for (let i = 1; i < trailRef.current.length; i++) {
        const alpha = i / trailRef.current.length;
        ctx.strokeStyle = `rgba(6, 182, 212, ${(alpha * 0.6).toFixed(2)})`;
        ctx.beginPath();
        ctx.moveTo(trailRef.current[i - 1].x * scale, trailRef.current[i - 1].y * scale);
        ctx.lineTo(trailRef.current[i].x * scale, trailRef.current[i].y * scale);
        ctx.stroke();
      }
    }

    // 5-6. Update interaction decay map
    for (const agent of frame.agents) {
      if (!agent.alive) continue;
      const prev = interactionDecay.current.get(agent.id) || { share: 0, attack: 0 };
      interactionDecay.current.set(agent.id, {
        share: Math.max(agent.shareReceived, prev.share * 0.88),
        attack: Math.max(agent.attackReceived, prev.attack * 0.88),
      });
    }

    // 7. Agents
    for (const agent of frame.agents) {
      const ax = agent.x * scale;
      const ay = agent.y * scale;
      const isSelected = agent.id === selectedAgentRef.current;
      const speciesHue = (agent.speciesId * 137.5) % 360;
      const speciesColor = `hsl(${speciesHue}, 70%, 55%)`;

      // 8. Dead agents
      if (!agent.alive) {
        ctx.globalAlpha = 0.35;
        ctx.strokeStyle = '#6b7280';
        ctx.lineWidth = 1.5;
        const s = 3;
        ctx.beginPath();
        ctx.moveTo(ax - s, ay - s);
        ctx.lineTo(ax + s, ay + s);
        ctx.moveTo(ax + s, ay - s);
        ctx.lineTo(ax - s, ay + s);
        ctx.stroke();
        ctx.globalAlpha = 1.0;
        continue;
      }

      const energyFrac = 1 - 1 / (1 + agent.energy);
      const radius = 4 + Math.min(energyFrac, 1) * 5;

      // Trail push (world coords)
      if (isSelected && agent.alive) {
        trailRef.current.push({ x: agent.x, y: agent.y });
        if (trailRef.current.length > MAX_TRAIL) trailRef.current.shift();
      }

      // Selected agent ray visualization
      if (isSelected) {
        const rayCount = 8;
        const raySpread = Math.PI * 0.8;
        const effectiveRayMax = 10 * (0.3 + 0.7 * light);
        ctx.strokeStyle = 'rgba(6, 182, 212, 0.2)';
        ctx.lineWidth = 1;
        for (let r = 0; r < rayCount; r++) {
          const rayAngle = agent.heading + (r - rayCount / 2) * raySpread / rayCount;
          const rx = Math.cos(rayAngle) * effectiveRayMax * scale;
          const ry = Math.sin(rayAngle) * effectiveRayMax * scale;
          ctx.beginPath();
          ctx.moveTo(ax, ay);
          ctx.lineTo(ax + rx, ay + ry);
          ctx.stroke();
        }
      }

      // Glow
      const glowRadius = isSelected ? radius * 3.5 : radius * 1.8;
      const glowA = isSelected ? 0.5 : 0.2;
      const glowGrad = ctx.createRadialGradient(ax, ay, 0, ax, ay, glowRadius);
      glowGrad.addColorStop(0, `hsla(${speciesHue}, 70%, 55%, ${glowA})`);
      glowGrad.addColorStop(1, 'rgba(0,0,0,0)');
      ctx.fillStyle = glowGrad;
      ctx.beginPath();
      ctx.arc(ax, ay, glowRadius, 0, Math.PI * 2);
      ctx.fill();

      // Wedge shape
      drawWedge(ctx, ax, ay, agent.heading, radius, speciesColor);

      // Selection outline
      if (isSelected) {
        ctx.strokeStyle = '#ffffff';
        ctx.lineWidth = 2;
        ctx.stroke();
      }

      // Signal ring
      const sigMag = Math.abs(agent.signal0) + Math.abs(agent.signal1);
      if (sigMag > 0.05) {
        const sigHue = ((agent.signal0 + 1) / 2) * 360;
        const sigSat = Math.min(Math.abs(agent.signal1) * 100, 100);
        ctx.strokeStyle = `hsla(${sigHue}, ${sigSat}%, 60%, ${Math.min(sigMag, 1)})`;
        ctx.lineWidth = 1.5;
        ctx.beginPath();
        ctx.arc(ax, ay, radius + 4, 0, Math.PI * 2);
        ctx.stroke();
      }

      // Persistent interaction rings (from decay map)
      const decay = interactionDecay.current.get(agent.id);
      if (decay) {
        if (decay.attack > 0.001) {
          ctx.strokeStyle = `rgba(255, 56, 100, ${Math.min(decay.attack * 15, 0.9)})`;
          ctx.lineWidth = 2;
          ctx.beginPath();
          ctx.arc(ax, ay, radius + 6, 0, Math.PI * 2);
          ctx.stroke();
        }
        if (decay.share > 0.001) {
          ctx.strokeStyle = `rgba(0, 246, 161, ${Math.min(decay.share * 15, 0.9)})`;
          ctx.lineWidth = 2;
          ctx.beginPath();
          ctx.arc(ax, ay, radius + 8, 0, Math.PI * 2);
          ctx.stroke();
        }
      }

      // Energy bar
      const barW = 12;
      const barH = 2;
      const barX = ax - barW / 2;
      const barY = ay - radius - 5;
      ctx.fillStyle = 'rgba(55, 65, 81, 0.6)';
      ctx.fillRect(barX, barY, barW, barH);
      const eHue = Math.floor(Math.min(energyFrac * 1.5, 1) * 120);
      ctx.fillStyle = `hsl(${eHue}, 70%, 50%)`;
      ctx.fillRect(barX, barY, barW * energyFrac, barH);

      // Species label
      if (isSelected || currentSpeed <= 0.25) {
        ctx.fillStyle = 'rgba(255, 255, 255, 0.7)';
        ctx.font = '8px monospace';
        ctx.textAlign = 'center';
        ctx.fillText(`S${agent.speciesId}`, ax, ay - radius - 8);
      }
    }

    // 9. Night overlay
    const nightAlpha = 0.55 * (1 - light);
    if (nightAlpha > 0.01) {
      ctx.fillStyle = `rgba(5, 5, 25, ${nightAlpha.toFixed(3)})`;
      ctx.fillRect(0, 0, CS, CS);
    }

    // 10. World border
    const borderBright = Math.round(40 + 170 * light);
    ctx.strokeStyle = `rgb(6, ${borderBright}, ${Math.round(borderBright * 0.85)})`;
    ctx.lineWidth = 2;
    ctx.strokeRect(1, 1, frame.worldWidth * scale - 2, frame.worldHeight * scale - 2);
  }, [frame, canvasSize, speed]);

  useEffect(() => {
    draw();
  }, [draw]);

  const handleClick = useCallback((e: React.MouseEvent<HTMLCanvasElement>) => {
    if (!frame) return;
    const canvas = canvasRef.current;
    if (!canvas) return;
    const rect = canvas.getBoundingClientRect();
    const scaleRatio = canvasSize / canvas.clientWidth;
    const x = (e.clientX - rect.left) * scaleRatio;
    const y = (e.clientY - rect.top) * scaleRatio;
    const worldSize = Math.max(frame.worldWidth, frame.worldHeight);
    const scale = canvasSize / worldSize;
    for (const agent of frame.agents) {
      const ax = agent.x * scale;
      const ay = agent.y * scale;
      const dist = Math.sqrt((x - ax) ** 2 + (y - ay) ** 2);
      if (dist < 15) {
        onSelectAgent(agent.id);
        return;
      }
    }
  }, [frame, onSelectAgent, canvasSize]);

  if (!frame) {
    return (
      <div ref={containerRef} className="w-full aspect-square bg-[var(--color-surface-alt)] rounded flex items-center justify-center text-[var(--color-text-muted)]">
        Waiting for simulation data...
      </div>
    );
  }

  return (
    <div ref={containerRef} className="w-full">
      <canvas
        ref={canvasRef}
        width={canvasSize}
        height={canvasSize}
        onClick={handleClick}
        className="w-full rounded cursor-crosshair border border-[var(--color-border)]"
        style={{ aspectRatio: '1 / 1' }}
      />
    </div>
  );
}
