export interface AgentDto {
  id: number;
  x: number;
  y: number;
  heading: number;
  energy: number;
  alive: boolean;
  speed: number;
  speciesId: number;
  signal0: number;
  signal1: number;
  shareReceived: number;
  attackReceived: number;
}

export interface FoodDto {
  id: number;
  x: number;
  y: number;
  value: number;
}

export interface ObstacleDto {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface HazardDto {
  x: number;
  y: number;
  width: number;
  height: number;
  damage: number;
}

export interface WorldFrameDto {
  tick: number;
  generation: number;
  worldIndex: number;
  worldWidth: number;
  worldHeight: number;
  agents: AgentDto[];
  food: FoodDto[];
  obstacles: ObstacleDto[];
  hazards: HazardDto[];
  foodEnergyMultiplier: number;
}

export interface BrainNodeDto {
  id: number;
  type: string;
  x: number;
  y: number;
  activation: number;
  label: string | null;
}

export interface BrainEdgeDto {
  from: number;
  to: number;
  weight: number;
  type: string;
  delay: number;
}

export interface BrainSnapshotDto {
  agentId: number;
  nodes: BrainNodeDto[];
  edges: BrainEdgeDto[];
}

export interface GenerationStatsDto {
  generation: number;
  bestFitness: number;
  meanFitness: number;
  worstFitness: number;
  speciesCount: number;
  populationSize: number;
  modulatoryEdgeCount: number;
  avgDelay: number;
}

export interface SimulationStatusDto {
  isRunning: boolean;
  isPaused: boolean;
  currentGeneration: number;
  currentTick: number;
  currentRound: number;
  speed: number;
  populationSize: number;
  speciesCount: number;
  aliveCount: number;
}


