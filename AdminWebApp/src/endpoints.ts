export const PUBLIC_ENDPOINTS = [
  '/time/submit/',
  '/metrics/checkpointUnlock/ZiplineCheckpointUnlocked/',
  '/metrics/checkpointUnlock/BoulderTunnelCheckpointUnlocked/',
  '/metrics/checkpointUnlock/crushingWallsCheckpointUnlocked/',
  '/metrics/checkpointUnlock/jumpRoomCheckpointUnlocked/',
  '/metrics/genericMetric/',
  '/data/top10/',
  '/data/top100/',
  '/data/page/',
  '/data/personal/',
] as const

export const ADMIN_ENDPOINTS = [
  '/admin/logs',
  '/admin/logs/',
  '/admin/players',
  '/admin/players/',
  '/admin/ratelimits',
  '/admin/ratelimits/',
  '/admin/checkpointunlocks',
  '/admin/checkpointunlocks/',
] as const

export const VALID_ENDPOINTS = {
  public: [...PUBLIC_ENDPOINTS],
  admin: [...ADMIN_ENDPOINTS],
}
