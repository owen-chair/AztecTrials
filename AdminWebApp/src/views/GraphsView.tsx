import type { DBMetricsStatsResult, LogStatsResult } from '../types'
import { EndpointMultiSelect } from '../components/EndpointMultiSelect'
import { VALID_ENDPOINTS } from '../endpoints'

import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
} from 'chart.js'
import { Line } from 'react-chartjs-2'

ChartJS.register(CategoryScale, LinearScale, PointElement, LineElement, Title, Tooltip, Legend)

const BYTES_PER_MB = 1024 * 1024

function bytesToMB(bytes: number) {
  return bytes / BYTES_PER_MB
}

function formatMB(v: number) {
  if (!Number.isFinite(v)) return '0'
  return v.toFixed(2)
}

function formatLabel(iso: string, bucket: 'sec' | 'min' | 'hour' | 'day') {
  const d = new Date(iso)
  if (bucket === 'day') return d.toISOString().slice(0, 10)
  if (bucket === 'hour') return d.toISOString().slice(0, 13).replace('T', ' ') + ':00Z'
  if (bucket === 'min') return d.toISOString().slice(0, 16).replace('T', ' ') + 'Z'
  return d.toISOString().slice(0, 19).replace('T', ' ') + 'Z'
}

function formatLabelAny(iso: string, bucket: 'sec' | 'min' | '30min' | 'hour' | 'day') {
  if (bucket === '30min') return formatLabel(iso, 'min')
  return formatLabel(iso, bucket)
}

export function GraphsView(props: {
  busy: boolean
  tab: 'network' | 'database'
  setTab: (v: 'network' | 'database') => void
  rangeMode: 'period' | 'range'
  setRangeMode: (v: 'period' | 'range') => void
  networkBucket: 'sec' | 'min' | 'hour' | 'day'
  setNetworkBucket: (v: 'sec' | 'min' | 'hour' | 'day') => void
  databaseBucket: '30min' | 'hour' | 'day'
  setDatabaseBucket: (v: '30min' | 'hour' | 'day') => void
  periodPreset: '' | '15m' | '1h' | '6h' | '24h' | '7d'
  setPeriodPreset: (v: '' | '15m' | '1h' | '6h' | '24h' | '7d') => void
  rangeStartDate: string
  setRangeStartDate: (v: string) => void
  rangeEndDate: string
  setRangeEndDate: (v: string) => void
  endpoints: string[]
  setEndpoints: (v: string[] | ((cur: string[]) => string[])) => void
  errorOnly: boolean
  setErrorOnly: (v: boolean) => void
  splitSuccessErrors: boolean
  setSplitSuccessErrors: (v: boolean) => void
  result: LogStatsResult | null
  errorsResult: LogStatsResult | null
  dbSizeResult: DBMetricsStatsResult | null
  dbRowsResult: DBMetricsStatsResult | null
  diskFreeResult: DBMetricsStatsResult | null
  onRefresh: () => void
}) {
  const bucket = props.tab === 'database' ? props.databaseBucket : props.networkBucket
  const canRefresh =
    props.rangeMode === 'period'
      ? props.periodPreset !== ''
      : props.rangeStartDate.trim() !== '' || props.rangeEndDate.trim() !== ''
  const totalPoints = props.result?.points ?? []
  const errorPoints = props.errorsResult?.points ?? []

  const points = totalPoints
  const labels = points.map((p) => formatLabelAny(p.timeutc, bucket))

  const totalValues = points.map((p) => p.count)
  const errorsByTime = new Map(errorPoints.map((p) => [p.timeutc, p.count]))
  const errorValues = points.map((p) => errorsByTime.get(p.timeutc) ?? 0)
  const successValues = points.map((p, idx) => Math.max(0, p.count - errorValues[idx]))

  const totalRequests = totalValues.reduce((acc, v) => acc + v, 0)
  const totalErrors = errorValues.reduce((acc, v) => acc + v, 0)
  const maxRequests = totalValues.reduce((acc, v) => Math.max(acc, v), 0)
  const maxErrors = errorValues.reduce((acc, v) => Math.max(acc, v), 0)

  const chartData = {
    labels,
    datasets: [
      ...(props.splitSuccessErrors
        ? [
            {
              label: 'Success',
              data: successValues,
              borderColor: '#0d6efd',
              backgroundColor: 'rgba(13,110,253,0.12)',
              pointRadius: 0,
              pointHitRadius: 12,
              pointHoverRadius: 4,
              tension: 0.2,
              fill: true,
            },
            {
              label: 'Errors',
              data: errorValues,
              borderColor: '#dc3545',
              backgroundColor: 'rgba(220,53,69,0.12)',
              pointRadius: 0,
              pointHitRadius: 12,
              pointHoverRadius: 4,
              tension: 0.2,
              fill: true,
            },
          ]
        : [
            {
              label: 'Requests',
              data: totalValues,
              borderColor: '#6ea8fe',
              backgroundColor: 'rgba(110,168,254,0.15)',
              pointRadius: 0,
              pointHitRadius: 12,
              pointHoverRadius: 4,
              tension: 0.2,
              fill: true,
            },
          ]),
    ],
  }

  const chartOptions = {
    responsive: true,
    maintainAspectRatio: false,
    interaction: {
      mode: 'index',
      intersect: false,
    },
    plugins: {
      legend: { display: false },
      title: { display: false },
      tooltip: {
        enabled: true,
        mode: 'index',
        intersect: false,
      },
    },
    scales: {
      x: {
        ticks: {
          maxTicksLimit: 10,
          autoSkip: true,
        },
        grid: { display: false },
      },
      y: {
        beginAtZero: true,
        ticks: { precision: 0 },
      },
    },
  } satisfies Parameters<typeof Line>[0]['options']

  const metricMBChartOptions = {
    ...chartOptions,
    scales: {
      ...chartOptions.scales,
      y: {
        ...chartOptions.scales.y,
        ticks: { precision: 2 },
      },
    },
  } satisfies Parameters<typeof Line>[0]['options']

  function buildMetricChart(
    result: DBMetricsStatsResult | null,
    label: string,
    borderColor: string,
    bg: string,
    valueTransform?: (v: number) => number,
  ) {
    const pts = result?.points ?? []
    const transform = valueTransform ?? ((v: number) => v)
    return {
      points: pts,
      labels: pts.map((p) => formatLabelAny(p.timeutc, bucket)),
      data: {
        labels: pts.map((p) => formatLabelAny(p.timeutc, bucket)),
        datasets: [
          {
            label,
            data: pts.map((p) => transform(p.value)),
            borderColor,
            backgroundColor: bg,
            pointRadius: 0,
            pointHitRadius: 12,
            pointHoverRadius: 4,
            tension: 0.2,
            fill: true,
          },
        ],
      },
      latest: pts.length > 0 ? transform(pts[pts.length - 1].value) : 0,
      max: pts.reduce((acc, p) => Math.max(acc, transform(p.value)), 0),
    }
  }

  const dbSizeChart = buildMetricChart(
    props.dbSizeResult,
    'DB size (MB)',
    '#6f42c1',
    'rgba(111,66,193,0.15)',
    bytesToMB,
  )
  const dbRowsChart = buildMetricChart(props.dbRowsResult, 'DB rows', '#20c997', 'rgba(32,201,151,0.15)')
  const diskFreeChart = buildMetricChart(
    props.diskFreeResult,
    'Free disk (MB)',
    '#fd7e14',
    'rgba(253,126,20,0.15)',
    bytesToMB,
  )

  return (
    <div className="card">
      <div className="card-body">
        <ul className="nav nav-tabs">
          <li className="nav-item">
            <button
              type="button"
              className={`nav-link ${props.tab === 'network' ? 'active' : ''}`}
              disabled={props.busy}
              onClick={() => props.setTab('network')}
            >
              Network
            </button>
          </li>
          <li className="nav-item">
            <button
              type="button"
              className={`nav-link ${props.tab === 'database' ? 'active' : ''}`}
              disabled={props.busy}
              onClick={() => props.setTab('database')}
            >
              Database
            </button>
          </li>
        </ul>

        <div className="row align-items-end">
          <div className="col-12 col-md-6 col-lg-2">
            <label className="form-label">Mode</label>
            <select
              className="form-select"
              value={props.rangeMode}
              disabled={props.busy}
              onChange={(e) => props.setRangeMode(e.target.value as any)}
            >
              <option value="period">Period</option>
              <option value="range">Date range</option>
            </select>
          </div>

          <div className="col-12 col-md-6 col-lg-2">
            <label className="form-label">Bucket</label>
            <select
              className="form-select"
              value={bucket}
              disabled={props.busy}
              onChange={(e) =>
                props.tab === 'database'
                  ? props.setDatabaseBucket(e.target.value as any)
                  : props.setNetworkBucket(e.target.value as any)
              }
            >
              {props.tab === 'network' ? (
                <>
                  <option value="sec">Per second</option>
                  <option value="min">Per minute</option>
                </>
              ) : (
                <option value="30min">Per 30 minutes</option>
              )}
              <option value="hour">Per hour</option>
              <option value="day">Per day</option>
            </select>
          </div>

          <div className="col-12 col-md-6 col-lg-2">
            <label className="form-label">Period</label>
            {props.rangeMode === 'period' ? (
              <select
                className="form-select"
                value={props.periodPreset}
                disabled={props.busy}
                onChange={(e) => props.setPeriodPreset(e.target.value as any)}
              >
                <option value="">(select)</option>
                <option value="15m">Last 15 minutes</option>
                <option value="1h">Last 1 hour</option>
                <option value="6h">Last 6 hours</option>
                <option value="24h">Last 24 hours</option>
                <option value="7d">Last 7 days</option>
              </select>
            ) : (
              <div className="text-body-secondary" style={{ paddingTop: 6 }}>
                —
              </div>
            )}
          </div>

          {props.rangeMode === 'range' ? (
            <>
              <div className="col-12 col-md-6 col-lg-3">
                <label className="form-label">Start</label>
                <input
                  type="date"
                  className="form-control"
                  disabled={props.busy}
                  value={props.rangeStartDate}
                  onChange={(e) => props.setRangeStartDate(e.target.value)}
                />
              </div>

              <div className="col-12 col-md-6 col-lg-3">
                <label className="form-label">End</label>
                <input
                  type="date"
                  className="form-control"
                  disabled={props.busy}
                  value={props.rangeEndDate}
                  onChange={(e) => props.setRangeEndDate(e.target.value)}
                />
              </div>
            </>
          ) : null}

          {props.tab === 'network' ? (
            <>
              <div className="col-12 col-md-6 col-lg-4">
                <EndpointMultiSelect
                  label="Endpoints"
                  groups={[
                    { label: 'Public', endpoints: VALID_ENDPOINTS.public },
                    { label: 'Admin', endpoints: VALID_ENDPOINTS.admin },
                  ]}
                  selected={props.endpoints}
                  setSelected={props.setEndpoints}
                  disabled={props.busy}
                />
              </div>

              <div className="col-12 col-md-6 col-lg-2">
                <label className="form-label">Errors</label>
                <div className="form-check">
                  <input
                    className="form-check-input"
                    type="checkbox"
                    checked={props.errorOnly}
                    disabled={props.busy || props.splitSuccessErrors}
                    onChange={(e) => props.setErrorOnly(e.target.checked)}
                    id="graphsErrorOnly"
                  />
                  <label className="form-check-label" htmlFor="graphsErrorOnly">
                    Only errors
                  </label>
                </div>
              </div>

              <div className="col-12 col-md-6 col-lg-2">
                <label className="form-label">Series</label>
                <div className="form-check">
                  <input
                    className="form-check-input"
                    type="checkbox"
                    checked={props.splitSuccessErrors}
                    disabled={props.busy}
                    onChange={(e) => props.setSplitSuccessErrors(e.target.checked)}
                    id="graphsSplitSuccessErrors"
                  />
                  <label className="form-check-label" htmlFor="graphsSplitSuccessErrors">
                    Split success/errors
                  </label>
                </div>
              </div>
            </>
          ) : null}

          <div className="col-12 col-lg-2">
            <button
              type="button"
              className="btn btn-outline-primary"
              disabled={props.busy || !canRefresh}
              onClick={props.onRefresh}
            >
              Refresh
            </button>
          </div>
        </div>

        {props.tab === 'network' ? (
          <>
            <div className="mt-3">
              <div className="d-flex flex-wrap gap-3 text-body-secondary">
                <div>
                  <span className="me-2">Total</span>
                  <span className="font-monospace">{totalRequests}</span>
                </div>
                <div>
                  <span className="me-2">Max / bucket</span>
                  <span className="font-monospace">{maxRequests}</span>
                </div>
                {props.splitSuccessErrors ? (
                  <>
                    <div>
                      <span className="me-2">Errors</span>
                      <span className="font-monospace">{totalErrors}</span>
                    </div>
                    <div>
                      <span className="me-2">Max errors / bucket</span>
                      <span className="font-monospace">{maxErrors}</span>
                    </div>
                  </>
                ) : null}
              </div>
            </div>

            <div className="mt-3">
              {points.length === 0 ? (
                <div className="text-body-secondary">No data.</div>
              ) : (
                <div style={{ height: 300 }}>
                  <Line data={chartData} options={chartOptions} />
                </div>
              )}
            </div>
          </>
        ) : (
          <>
            <div className="mt-3">
              <div className="d-flex flex-wrap gap-3 text-body-secondary">
                <div>
                  <span className="me-2">Latest</span>
                  <span className="font-monospace">{formatMB(dbSizeChart.latest)}</span>
                </div>
                <div>
                  <span className="me-2">Max / bucket</span>
                  <span className="font-monospace">{formatMB(dbSizeChart.max)}</span>
                </div>
              </div>
              {dbSizeChart.points.length === 0 ? (
                <div className="text-body-secondary mt-2">No DB size data yet (wait for the hourly snapshot).</div>
              ) : (
                <div style={{ height: 200 }} className="mt-2">
                  <Line data={dbSizeChart.data} options={metricMBChartOptions} />
                </div>
              )}
            </div>

            <div className="mt-4">
              <div className="d-flex flex-wrap gap-3 text-body-secondary">
                <div>
                  <span className="me-2">Latest</span>
                  <span className="font-monospace">{dbRowsChart.latest}</span>
                </div>
                <div>
                  <span className="me-2">Max / bucket</span>
                  <span className="font-monospace">{dbRowsChart.max}</span>
                </div>
              </div>
              {dbRowsChart.points.length === 0 ? (
                <div className="text-body-secondary mt-2">No DB rows data yet (wait for the hourly snapshot).</div>
              ) : (
                <div style={{ height: 200 }} className="mt-2">
                  <Line data={dbRowsChart.data} options={chartOptions} />
                </div>
              )}
            </div>

            <div className="mt-4">
              <div className="d-flex flex-wrap gap-3 text-body-secondary">
                <div>
                  <span className="me-2">Latest</span>
                  <span className="font-monospace">{formatMB(diskFreeChart.latest)}</span>
                </div>
                <div>
                  <span className="me-2">Max / bucket</span>
                  <span className="font-monospace">{formatMB(diskFreeChart.max)}</span>
                </div>
              </div>
              {diskFreeChart.points.length === 0 ? (
                <div className="text-body-secondary mt-2">No disk data yet (wait for the hourly snapshot).</div>
              ) : (
                <div style={{ height: 200 }} className="mt-2">
                  <Line data={diskFreeChart.data} options={metricMBChartOptions} />
                </div>
              )}
            </div>
          </>
        )}
      </div>
    </div>
  )
}
