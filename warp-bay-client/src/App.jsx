import { useCallback, useEffect, useMemo, useState } from 'react'
import './App.css'

const API = import.meta.env.VITE_API_URL || 'http://localhost:5123'
const DEMO_CREDS = [
  { role: 'Admin', email: 'admin@warp-bay.demo', password: 'Demo123!' },
  { role: 'Manager', email: 'manager@warp-bay.demo', password: 'Demo123!' },
  { role: 'Tech', email: 'tech@warp-bay.demo', password: 'Demo123!' },
  { role: 'Customer', email: 'customer@warp-bay.demo', password: 'Demo123!' },
]
const NEXT = ['Confirmed', 'CheckedIn', 'InService', 'Ready', 'PickedUp']

function avatarColor(name) {
  let h = 0
  for (const c of name) h = (h * 31 + c.charCodeAt(0)) % 360
  return `hsl(${h} 80% 60%)`
}
function fmt(dt) {
  return new Date(dt).toLocaleString([], { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
}
async function api(path, { method = 'GET', body, token } = {}) {
  const res = await fetch(`${API}${path}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(method === 'POST' && path.includes('appointments')
        ? { 'Idempotency-Key': crypto.randomUUID() } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  })
  const data = await res.json().catch(() => ({}))
  if (!res.ok) throw new Error(data.error || `HTTP ${res.status}`)
  return data
}

export default function App() {
  const [tab, setTab] = useState('book')
  const [token, setToken] = useState(() => localStorage.getItem('wb-token') || '')
  const [me, setMe] = useState(null)
  const [services, setServices] = useState([])
  const [serviceId, setServiceId] = useState('')
  const [date, setDate] = useState(() => new Date().toISOString().slice(0, 10))
  const [slots, setSlots] = useState([])
  const [slot, setSlot] = useState(null)
  const [form, setForm] = useState({ fullName: '', phone: '', plate: '', notes: '' })
  const [msg, setMsg] = useState('')
  const [appts, setAppts] = useState([])
  const [report, setReport] = useState(null)

  const auth = useMemo(() => ({ token }), [token])
  const login = async (email, password) => {
    setMsg('')
    try {
      const r = await api('/api/auth/login', { method: 'POST', body: { email, password } })
      setToken(r.token); localStorage.setItem('wb-token', r.token); setMe(r.user)
    } catch (e) { setMsg(e.message) }
  }
  const demoLogin = async (role) => {
    setMsg('')
    try {
      const r = await api('/api/auth/demo', { method: 'POST', body: { role } })
      setToken(r.token); localStorage.setItem('wb-token', r.token); setMe(r.user); setTab('schedule')
    } catch (e) { setMsg(e.message) }
  }
  const logout = () => { setToken(''); setMe(null); localStorage.removeItem('wb-token') }

  useEffect(() => {
    fetch(`${API}/api/services`).then(r => r.json()).then(d => {
      setServices(d); if (d[0]) setServiceId(d[0].id)
    }).catch(() => {})
  }, [])
  useEffect(() => {
    if (!token) return
    fetch(`${API}/api/me`, { headers: { Authorization: `Bearer ${token}` } })
      .then(r => (r.ok ? r.json() : null)).then(setMe).catch(() => {})
  }, [token])

  const loadSlots = useCallback(async () => {
    if (!serviceId || !date) return
    setMsg('Loading warp slots…')
    try {
      const d = await api(`/api/availability?serviceId=${serviceId}&date=${date}`)
      setSlots(d); setSlot(null); setMsg(d.length ? '' : 'No slots — try another day.')
    } catch (e) { setMsg(e.message) }
  }, [serviceId, date])
  useEffect(() => { loadSlots() }, [loadSlots])

  const book = async (e) => {
    e.preventDefault()
    if (!slot) { setMsg('Pick a slot first.'); return }
    setMsg('Booking…')
    try {
      const r = await api('/api/appointments', {
        method: 'POST',
        body: {
          serviceId, bayId: slot.bayId, techId: slot.techId, slotStartUtc: slot.startUtc,
          fullName: form.fullName, phone: form.phone, plate: form.plate || null, notes: form.notes || null,
        },
      })
      setMsg(`Booked! ${fmt(r.slotStartUtc)} · ${slot.bayName}.`)
      loadSlots()
    } catch (e2) { setMsg(e2.message) }
  }

  const loadSchedule = useCallback(async () => {
    if (!token) return
    try {
      const d = await api(`/api/appointments?day=${date}&page=1&pageSize=50`, auth)
      setAppts(d.items)
    } catch (e) { setMsg(e.message) }
  }, [token, date, auth])
  useEffect(() => { if (tab === 'schedule') loadSchedule() }, [tab, loadSchedule])

  const advance = async (id, to) => {
    try {
      await api(`/api/appointments/${id}/status`, { method: 'PATCH', body: { to }, token })
      loadSchedule()
    } catch (e) { setMsg(e.message) }
  }
  const loadReport = async () => {
    try { setReport(await api(`/api/admin/reports/day?date=${date}`, auth)) }
    catch (e) { setMsg(e.message) }
  }
  const resetDemo = async () => {
    try { await api('/api/demo/reset', { method: 'POST' }); setMsg('Demo data reset.'); loadSlots(); loadSchedule() }
    catch (e) { setMsg(e.message) }
  }

  return (
    <div className="wb">
      <header className="wb-head">
        <img src="/logo.svg" alt="Warp Bay Auto Lab logo" width="52" height="52" />
        <div>
          <h1>Warp Bay Auto Lab</h1>
          <p>Neon-grade scheduling · Flagship <b>WARP BAY-07</b> · fictional demo — no real customers</p>
        </div>
        <nav>
          {[['book', 'Book'], ['schedule', 'Schedule'], ['report', 'Report'], ['login', me ? me.role : 'Login']].map(([k, l]) => (
            <button key={k} className={tab === k ? 'on' : ''} onClick={() => setTab(k)}>{l}</button>
          ))}
          {token && <button onClick={logout}>Logout</button>}
        </nav>
      </header>

      {msg && <div className="wb-msg">{msg}</div>}

      {tab === 'book' && (
        <section className="wb-grid">
          <div className="card">
            <h2>1 · Service + day</h2>
            <label>Service
              <select value={serviceId} onChange={e => setServiceId(e.target.value)}>
                {services.map(s => <option key={s.id} value={s.id}>{s.name} · {s.durationMinutes} min · ${(s.priceCents / 100).toFixed(0)}</option>)}
              </select>
            </label>
            <label>Day
              <input type="date" value={date} onChange={e => setDate(e.target.value)} />
            </label>
            <button onClick={loadSlots}>Reload slots</button>
            <p className="hint">Tip: use plate <code>DEMO-409</code> to see the 409 double-booking guard.</p>
          </div>
          <div className="card">
            <h2>2 · Pick a warp slot</h2>
            <div className="slots">
              {slots.slice(0, 24).map((s, i) => (
                <button key={i} className={slot === s ? 'slot on' : 'slot'}
                  onClick={() => setSlot(s)}>
                  {new Date(s.startUtc).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })} · {s.bayName}
                </button>
              ))}
              {!slots.length && <p>No slots loaded.</p>}
            </div>
          </div>
          <form className="card" onSubmit={book}>
            <h2>3 · Your ride</h2>
            <input placeholder="Full name" required value={form.fullName} onChange={e => setForm({ ...form, fullName: e.target.value })} />
            <input placeholder="Phone (555) 010-1234" required value={form.phone} onChange={e => setForm({ ...form, phone: e.target.value })} />
            <input placeholder="Plate e.g. WRP-101" value={form.plate} onChange={e => setForm({ ...form, plate: e.target.value })} />
            <input placeholder="Notes" value={form.notes} onChange={e => setForm({ ...form, notes: e.target.value })} />
            <button type="submit">Book appointment</button>
          </form>
        </section>
      )}

      {tab === 'schedule' && (
        <section className="card">
          <h2>Day schedule · {date}</h2>
          <input type="date" value={date} onChange={e => setDate(e.target.value)} />
          {!token && <p>Login (one-click demo below) to view the board.</p>}
          <div className="board">
            {appts.map(a => (
              <div key={a.id} className="appt">
                <span className="avatar" style={{ background: avatarColor(a.id) }}>{a.status[0]}</span>
                <div>
                  <b>{fmt(a.slotStartUtc)} → {fmt(a.slotEndUtc)}</b>
                  <div className="sub">{a.status} · bay {a.bayId.slice(0, 4)}…</div>
                </div>
                {token && NEXT.includes(NEXT.find(n => n === a.status) || '') === false && null}
                {token && (
                  <select defaultValue="" onChange={e => e.target.value && advance(a.id, e.target.value)}>
                    <option value="">Advance…</option>
                    {NEXT.map(s => <option key={s} value={s}>{s}</option>)}
                    <option value="Cancelled">Cancelled</option>
                    <option value="NoShow">NoShow</option>
                  </select>
                )}
              </div>
            ))}
            {!appts.length && <p>No appointments for this day yet — book one from the Book tab.</p>}
          </div>
        </section>
      )}

      {tab === 'report' && (
        <section className="card">
          <h2>Manager report</h2>
          <p>Requires Manager/Admin demo login.</p>
          <button onClick={loadReport}>Load {date}</button>
          {report && (
            <ul>
              <li>Total: {report.total}</li>
              <li>Revenue: ${(report.revenueCents / 100).toFixed(0)}</li>
              <li>Utilization: {report.utilizationPct}%</li>
              <li>By status: {JSON.stringify(report.byStatus)}</li>
            </ul>
          )}
        </section>
      )}

      {tab === 'login' && (
        <section className="wb-grid">
          <form className="card" onSubmit={e => { e.preventDefault(); const f = new FormData(e.target); login(f.get('email'), f.get('password')) }}>
            <h2>Login</h2>
            <input name="email" placeholder="email" defaultValue="manager@warp-bay.demo" />
            <input name="password" type="password" placeholder="password" defaultValue="Demo123!" />
            <button type="submit">Login</button>
            {me && <p>Signed in as {me.name} ({me.role})</p>}
          </form>
          <div className="card">
            <h2>One-click demo (like Deadwax)</h2>
            {DEMO_CREDS.map(c => (
              <div key={c.role} className="demo-row">
                <span><b>{c.role}</b> · {c.email}</span>
                <button onClick={() => demoLogin(c.role)}>Login as {c.role}</button>
              </div>
            ))}
            <button className="danger" onClick={resetDemo}>Reset demo data</button>
          </div>
        </section>
      )}

      <footer>Warp Bay Auto Lab · fictional demo data · plates/phones/names invented · API: <code>{API}</code> · <code>/swagger</code> · <code>/health</code></footer>
    </div>
  )
}
