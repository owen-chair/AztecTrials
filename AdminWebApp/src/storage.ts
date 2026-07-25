export type AppConfig = {
  adminKey: string
}

const KEY = 'aztectrials_admin_config_v1'

export function loadConfig(): AppConfig {
  try {
    const raw = localStorage.getItem(KEY)
    if (!raw) return { adminKey: '' }
    const parsed = JSON.parse(raw) as Partial<AppConfig>
    return {
      adminKey: typeof parsed.adminKey === 'string' ? parsed.adminKey : '',
    }
  } catch {
    return { adminKey: '' }
  }
}

export function saveConfig(cfg: AppConfig) {
  try {
    localStorage.setItem(KEY, JSON.stringify(cfg))
  } catch {
    // ignore
  }
}
