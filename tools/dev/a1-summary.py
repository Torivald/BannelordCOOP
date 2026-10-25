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
