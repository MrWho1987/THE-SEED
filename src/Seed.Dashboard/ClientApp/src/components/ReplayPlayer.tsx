import { useState, useCallback, useEffect, useRef } from 'react';
import type { WorldFrameDto } from '../types';

interface Props {
  frames: WorldFrameDto[];
  onFrameChange: (frame: WorldFrameDto) => void;
  onClose: () => void;
}

export function ReplayPlayer({ frames, onFrameChange, onClose }: Props) {
  const [currentIndex, setCurrentIndex] = useState(0);
  const [isPlaying, setIsPlaying] = useState(false);
  const [playbackSpeed, setPlaybackSpeed] = useState(1);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const currentFrame = frames[currentIndex];

  useEffect(() => {
    if (currentFrame) {
      onFrameChange(currentFrame);
    }
  }, [currentFrame, onFrameChange]);

  useEffect(() => {
    if (isPlaying) {
      intervalRef.current = setInterval(() => {
        setCurrentIndex(prev => {
          if (prev >= frames.length - 1) {
            setIsPlaying(false);
            return prev;
          }
          return prev + 1;
        });
      }, 33 / playbackSpeed);
    } else {
      if (intervalRef.current) {
        clearInterval(intervalRef.current);
        intervalRef.current = null;
      }
    }

    return () => {
      if (intervalRef.current) {
        clearInterval(intervalRef.current);
      }
    };
  }, [isPlaying, playbackSpeed, frames.length]);

  const handleSliderChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const index = parseInt(e.target.value, 10);
    setCurrentIndex(index);
  }, []);

  const stepBack = useCallback(() => {
    setCurrentIndex(prev => Math.max(0, prev - 1));
  }, []);

  const stepForward = useCallback(() => {
    setCurrentIndex(prev => Math.min(frames.length - 1, prev + 1));
  }, [frames.length]);

  const jumpToStart = useCallback(() => {
    setCurrentIndex(0);
  }, []);

  const jumpToEnd = useCallback(() => {
    setCurrentIndex(frames.length - 1);
  }, [frames.length]);

  if (frames.length === 0) {
    return null;
  }

  return (
    <div className="border-t border-[var(--color-border)] px-4 py-3 bg-[var(--color-surface)]">
      <div className="flex items-center gap-4">
        {/* Playback Controls */}
        <div className="flex gap-2">
          <button
            onClick={jumpToStart}
            className="px-2 py-1 bg-[var(--color-surface-alt)] hover:bg-[var(--color-border)] rounded"
            title="Jump to start"
          >
            ⏮
          </button>
          <button
            onClick={stepBack}
            className="px-2 py-1 bg-[var(--color-surface-alt)] hover:bg-[var(--color-border)] rounded"
            title="Step back"
          >
            ⏪
          </button>
          <button
            onClick={() => setIsPlaying(!isPlaying)}
            className={`px-3 py-1 rounded font-semibold ${
              isPlaying 
                ? 'bg-[var(--color-warning)] hover:bg-[var(--color-warning)]/80' 
                : 'bg-[var(--color-success)] hover:bg-[var(--color-success)]/80'
            }`}
          >
            {isPlaying ? '⏸ Pause' : '▶ Play'}
          </button>
          <button
            onClick={stepForward}
            className="px-2 py-1 bg-[var(--color-surface-alt)] hover:bg-[var(--color-border)] rounded"
            title="Step forward"
          >
            ⏩
          </button>
          <button
            onClick={jumpToEnd}
            className="px-2 py-1 bg-[var(--color-surface-alt)] hover:bg-[var(--color-border)] rounded"
            title="Jump to end"
          >
            ⏭
          </button>
        </div>

        {/* Timeline Slider */}
        <div className="flex-1 flex items-center gap-3">
          <span className="text-sm text-[var(--color-text-muted)] w-24">
            Frame {currentIndex + 1} / {frames.length}
          </span>
          <input
            type="range"
            min="0"
            max={frames.length - 1}
            value={currentIndex}
            onChange={handleSliderChange}
            className="flex-1 accent-[var(--color-accent)]"
          />
        </div>

        {/* Speed Control */}
        <div className="flex items-center gap-2">
          <span className="text-sm text-[var(--color-text-muted)]">Speed:</span>
          <select
            value={playbackSpeed}
            onChange={(e) => setPlaybackSpeed(parseFloat(e.target.value))}
            className="bg-[var(--color-surface-alt)] border border-[var(--color-border)] rounded px-2 py-1 text-sm"
          >
            <option value="0.25">0.25x</option>
            <option value="0.5">0.5x</option>
            <option value="1">1x</option>
            <option value="2">2x</option>
            <option value="4">4x</option>
          </select>
        </div>

        {/* Frame Info */}
        {currentFrame && (
          <div className="text-sm text-[var(--color-text-muted)]">
            Gen {currentFrame.generation} | Tick {currentFrame.tick}
          </div>
        )}

        {/* Close Button */}
        <button
          onClick={onClose}
          className="px-3 py-1 bg-[var(--color-danger)] hover:bg-[var(--color-danger)]/80 rounded font-semibold"
        >
          ✕ Close
        </button>
      </div>
    </div>
  );
}

