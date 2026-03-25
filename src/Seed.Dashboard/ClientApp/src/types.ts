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
  isCorpse: boolean;
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
  lightLevel: number;
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
  wSlow: number;
  wFast: number;
  plasticityGain: number;
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
  avgDistanceTraveled: number;
  avgFoodCollected: number;
  avgSurvivalTicks: number;
  speciesBreakdown: SpeciesInfoDto[] | null;
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
  maxTicksPerRound: number;
  arenaRounds: number;
  overridesActive: boolean;
}

export interface RoundMetricsDto {
  round: number;
  survivalTicks: number;
  netEnergyDelta: number;
  foodCollected: number;
  distanceTraveled: number;
  fitness: number;
}

export interface SelectedAgentDetailsDto {
  agentId: number;
  connectionCount: number;
  hiddenNodeCount: number;
  totalNodeCount: number;
  survivalTicks: number;
  foodCollected: number;
  netEnergyDelta: number;
  distanceTraveled: number;
  instabilityPenalty: number;
  modReward: number;
  modPain: number;
  modCuriosity: number;
  roundHistory: RoundMetricsDto[];
  aggregatedFitness: number | null;
}

export interface SpeciesInfoDto {
  speciesId: number;
  memberCount: number;
  meanFitness: number;
}

export interface WorldOverrideDto {
  foodCount?: number;
  ambientEnergyRate?: number;
  corpseEnergyBase?: number;
  dayNightPeriod?: number;
  seasonPeriod?: number;
  hazardDamageMultiplier?: number;
  foodQualityVariation?: number;
  lightLevelOverride?: number;
}
