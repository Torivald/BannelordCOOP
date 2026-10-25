"""Summarises Spike A1 mod logs: python a1-summary.py <log> ... (5 s windows: fps, traffic, ack latency, apply cost, hits/deaths, errors)."""
import re, sys, statistics as st
PAT = re.compile(r'A1: (?P<ph>sync|baseline): (?:apply=(?P<apply>[\d.]+)ms/chunk x(?P<calls>\d+) own=(?P<own>[\d.]+)ms/frame\(max (?P<ownmax>[\d.]+)\) )?(?:moving=(?P<mv>\d+) )?(?:siege=[\d/]+ )?fps=(?P<fps>[\d.]+) maxDt=(?P<maxdt>[\d.]+)ms agents=(?P<ag>\d+) tx=(?P<tx>[\d.]+)KB/s rx=(?P<rx>[\d.]+)KB/s snap=(?P<snap>\d+) hits=(?P<hits>\d+) deaths=(?P<deaths>\d+) ack p50/p95/max=(?P<a50>[\d.]+)/(?P<a95>[\d.]+)/(?P<amax>[\d.]+)ms')
for path in sys.argv[1:]:
    rows, errors, role, mode, move = [], 0, '?', '', []
    for l in open(path, encoding='utf-8', errors='replace'):
        m = PAT.search(l)
        if m: rows.append(m.groupdict())
        if 'tick failed' in l: errors += 1
        m = re.search(r"role=(\w+).*mode='([^']*)'", l)
        if m: role, mode = m.groups()
        m = re.search(r'solo\((\w+)\)', l)
        if m and role == '?': role = 'solo-' + m.group(1)
        if 'movement visible' in l: move.append(re.search(r'(\d+) ms after', l).group(1) + ' ms')
    print(f"== {re.split(r'[\\/]', path)[-1]} role={role} mode='{mode}' windows={len(rows)} tickErrors={errors} inputToMovementLatency={move}")
    for ph in ('baseline', 'sync'):
        r = [x for x in rows if x['ph'] == ph]
        if not r: continue
        fps = [float(x['fps']) for x in r]
        print(f"  {ph}: n={len(r)} fps mean={st.mean(fps):.1f} median={st.median(fps):.1f} min={min(fps):.1f}; agents={r[-1]['ag']}; maxDt max={max(float(x['maxdt']) for x in r):.0f} ms")
        if ph == 'sync':
            print(f"  traffic tx mean={st.mean(float(x['tx']) for x in r):.1f} KB/s rx mean={st.mean(float(x['rx']) for x in r):.1f} KB/s; chunks applied/5s mean={st.mean(int(x['snap']) for x in r):.0f}; hits={r[-1]['hits']} deaths={r[-1]['deaths']}")
            ow = [(float(x['own']), float(x['ownmax'])) for x in r if x['own']]
            if ow: print(f"  own code per frame: mean={st.mean(o[0] for o in ow):.2f} ms, worst frame={max(o[1] for o in ow):.1f} ms")
            ap = [float(x['apply']) for x in r if x['apply'] and float(x['apply']) > 0]
            if ap: print(f"  apply cost per chunk (25 agents): mean={st.mean(ap):.2f} ms max={max(ap):.2f} ms")
            a = [(float(x['a50']), float(x['a95']), float(x['amax'])) for x in r if float(x['a50']) > 0]
            if a: print(f"  ack latency: p50 mean={st.mean(x[0] for x in a):.0f} ms, p95 mean={st.mean(x[1] for x in a):.0f} ms, max={max(x[2] for x in a):.0f} ms")

# ---- A1b additions: divergence, effect latency, local damage, stalls, GC (research 0013) ----
def a1b(path):
    div, eff, stalls, gcs, emu = [], None, [], [], ''
    for l in open(path, encoding='utf-8', errors='replace'):
        m = re.search(r'A1: div: alive host/client=(\d+)/(\d+) \(\|d\| max (\d+), mismatching samples (\d+)/(\d+)\) hp host/client=(\d+)/(\d+) \(\|d\| mean ([\d.]+) max ([\d.]+)\) posErr p50/p95/max=([\d.]+)/([\d.]+)/([\d.]+)m n=(\d+) localBlows=(\d+) \(total (\d+), dmg (\d+)\) localDeaths=(\d+) \(total (\d+)\)', l)
        if m: div.append([float(x) for x in m.groups()])
        m = re.search(r'effect\[(run|final)\]: start n=(\d+) p50/p95/max=(\d+)/(\d+)/(\d+)ms; stop n=(\d+) p50/p95/max=(\d+)/(\d+)/(\d+)ms(?:; start-free\(enemy>15m\) n=(\d+) p50/p95/max=(\d+)/(\d+)/(\d+)ms; start-near n=(\d+) p50/p95/max=(\d+)/(\d+)/(\d+)ms)?', l)
        if m: eff = m.groups()
        if 'STALL' in l: stalls.append(re.search(r'STALL frame=(\d+)ms.*', l).group(0)[:200])
        m = re.search(r'A1: gc: gen0/1/2 total=(\d+)/(\d+)/(\d+) managed=(\d+)MB', l)
        if m: gcs.append([int(x) for x in m.groups()])
        if 'network emulation ON' in l: emu = l.split('A1: ')[1].strip()
    if not (div or eff or stalls): return
    print(f"  A1b [{emu or 'no network emulation'}]")
    if div:
        print(f"    alive: max |host-client| over run = {max(d[2] for d in div):.0f}; windows with any mismatch = {sum(1 for d in div if d[3] > 0)}/{len(div)}; final alive host/client = {div[-1][0]:.0f}/{div[-1][1]:.0f}")
        print(f"    health sum |d|: mean of window means = {st.mean(d[7] for d in div):.1f}, max = {max(d[8] for d in div):.1f}; final hp host/client = {div[-1][5]:.0f}/{div[-1][6]:.0f}")
        print(f"    position error before correction (m): p50 median={st.median(d[9] for d in div):.2f} p95 median={st.median(d[10] for d in div):.2f} worst max={max(d[11] for d in div):.2f}")
        print(f"    local blows attempted on client (suppressed): {div[-1][14]:.0f}, damage {div[-1][15]:.0f}; local deaths: {div[-1][17]:.0f}")
    if eff:
        print(f"    input->visible effect (client, first position change): start n={eff[1]} p50/p95/max={eff[2]}/{eff[3]}/{eff[4]} ms; stop n={eff[5]} p50/p95/max={eff[6]}/{eff[7]}/{eff[8]} ms")
        if eff[9] is not None:
            print(f"      start, nearest enemy > 15 m: n={eff[9]} p50/p95/max={eff[10]}/{eff[11]}/{eff[12]} ms; start, enemy <= 15 m (melee): n={eff[13]} p50/p95/max={eff[14]}/{eff[15]}/{eff[16]} ms")
    if gcs:
        print(f"    GC gen0/1/2 at end {gcs[-1][0]}/{gcs[-1][1]}/{gcs[-1][2]}, managed {min(g[3] for g in gcs)}-{max(g[3] for g in gcs)} MB")
    print(f"    STALL lines (>200 ms frames): {len(stalls)}" + ''.join('\n      ' + s for s in stalls[:5]))
for path in sys.argv[1:]:
    print('==', re.split(r'[\/]', path)[-1], '(A1b)')
    a1b(path)
