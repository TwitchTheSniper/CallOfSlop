"""Adds the named link ports (and a detail stub box at each port) to the drivetrain, tank, battery and alternator part files
(board item np01). Idempotent: a part that already has "ports" is skipped. Run: python3 -I tools/constructs/make_ports.py"""
import json,glob,sys,itertools
import os
ROOT=os.path.join(os.path.dirname(os.path.abspath(__file__)),'..','..','Constructs','Parts')
AX={'+x':(0,1),'-x':(0,-1),'+y':(1,1),'-y':(1,-1),'+z':(2,1),'-z':(2,-1)}
def bounds(d):
    mn=[9e9]*3;mx=[-9e9]*3
    for b in d['boxes']:
        if b.get('detail'): continue
        if 'min' in b: lo,hi=b['min'],b['max']
        else:
            at=b.get('at',[0,0,0]);sz=b['size'];lo=[at[i]-sz[i]/2 for i in range(3)];hi=[at[i]+sz[i]/2 for i in range(3)]
        for i in range(3): mn[i]=min(mn[i],lo[i]);mx[i]=max(mx[i],hi[i])
    return mn,mx
def facept(d,face,off=(0,0)):
    mn,mx=bounds(d);a,s=AX[face]
    c=[(mn[i]+mx[i])/2 for i in range(3)]
    p=list(c);p[a]=mx[a] if s>0 else mn[a]
    o=[i for i in range(3) if i!=a]
    p[o[0]]+=off[0]*(mx[o[0]]-mn[o[0]]);p[o[1]]+=off[1]*(mx[o[1]]-mn[o[1]])
    return [round(x,4) for x in p]
def planes(b):
    if 'min' in b: lo,hi=b['min'],b['max']
    else:
        at=b.get('at',[0,0,0]);sz=b['size'];lo=[at[i]-sz[i]/2 for i in range(3)];hi=[at[i]+sz[i]/2 for i in range(3)]
    return lo,hi
def stub(d,pt,face,kind):
    a,s=AX[face]; o=[i for i in range(3) if i!=a]
    others=[planes(b) for b in d['boxes']]
    col={'shaft':'#6b7077','pipe':'#8a9099','power':'#26282c'}[kind]
    for t in itertools.product([0.052,0.057,0.061,0.066,0.071,0.049,0.045],[0.043,0.047,0.052,0.038,0.056],[0.012,0.017,0.021,0.025]):
        half,out,inn=t
        lo=list(pt);hi=list(pt)
        for i in o: lo[i]=pt[i]-half;hi[i]=pt[i]+half
        if s>0: lo[a]=pt[a]-inn;hi[a]=pt[a]+out
        else: lo[a]=pt[a]-out;hi[a]=pt[a]+inn
        if any(abs(x-y)<0.002 for (l2,h2) in others for i in range(3) for x in (lo[i],hi[i]) for y in (l2[i],h2[i])): continue
        ctr=[round((lo[i]+hi[i])/2,4) for i in range(3)];sz=[round(hi[i]-lo[i],4) for i in range(3)]
        return {'size':sz,'at':ctr,'color':col,'detail':True}
    raise Exception('no stub fits '+d['name']+str(pt))
def sock(d,name):
    for s in d['sockets']:
        if s['name']==name: return s
    raise Exception('no socket '+name+' in '+d['name'])
def shaft(d,name,sk,dr,face=None,at=None):
    s=sock(d,sk);p={'name':name,'type':'shaft','dir':dr,'socket':sk}
    if at: p['at']=at;p['face']=face or s['dir']
    return p
def build(d,f):
    r=d['role'];ps=[];base=f.split('/')[-1]
    drv=d.get('drive',{})
    if d.get('legacy'): return None
    if r=='engine':
        ps.append(shaft(d,'Drive out','+z','out'))
        fuel=d['engine'].get('fuel')
        if fuel in (None,'fuel','petrol','diesel'):
            ps.append({'name':'Fuel in','type':'pipe','dir':'in','fluid':'fuel','at':facept(d,'-x'),'face':'-x'})
            ps.append({'name':'Exhaust out','type':'pipe','dir':'out','fluid':'exhaust','noEffect':True,'at':facept(d,'+x'),'face':'+x'})
            ps.append({'name':'Coolant in','type':'pipe','dir':'in','fluid':'coolant','noEffect':True,'at':facept(d,'+y',(0,-0.25)),'face':'+y'})
            ps.append({'name':'Coolant out','type':'pipe','dir':'out','fluid':'coolant','noEffect':True,'at':facept(d,'+y',(0,0.25)),'face':'+y'})
    elif r=='fueltank':
        ps.append({'name':'Fuel out','type':'pipe','dir':'out','fluid':d['fuelTank'].get('fluid','fuel'),'at':facept(d,'+x'),'face':'+x'})
        ps.append({'name':'Fill','type':'pipe','dir':'in','fluid':d['fuelTank'].get('fluid','fuel'),'noEffect':True,'at':facept(d,'+y'),'face':'+y'})
    elif r=='battery':
        ps.append({'name':'Power out','type':'power','dir':'out','at':facept(d,'+x'),'face':'+x'})
        ps.append({'name':'Power in','type':'power','dir':'in','noEffect':True,'at':facept(d,'-x'),'face':'-x'})
    elif r=='alternator':
        ps.append({'name':'Power out','type':'power','dir':'out','at':facept(d,'+x'),'face':'+x'})
    elif r=='transmission':
        ps+= [shaft(d,'Input shaft','-z','in'),shaft(d,'Output shaft','+z','out')]
    elif r=='differential':
        ps+= [shaft(d,'Input shaft','-z0','in',face='-z',at=facept(d,'-z')),shaft(d,'Output left','-x','out',face='-x',at=facept(d,'-x')),shaft(d,'Output right','+x','out',face='+x',at=facept(d,'+x'))]
    elif r=='transfer':
        k=drv.get('kind')
        m={'belt':[('Input shaft','in','in'),('Output shaft','out','out')],'chain':[('Input shaft','in','in'),('Output shaft','out','out')],
           'clutch':[('Input shaft','-z','in'),('Output shaft','+z','out')],'gearbox':None,'cvjoint':[('Input shaft','-x','in'),('Output shaft','+x','out')],
           'ujoint':[('Input shaft','-x','in'),('Output shaft','+x','out')],
           'transfercase':[('Input shaft','in','in'),('Rear output','rear','out'),('Front output','front','out')],
           'pto':[('Input shaft','-z','in'),('Output shaft','+z','out'),('Take-off shaft','+y','out')],
           'hub':[('Drive in','-z','in')],'stub':[('Drive in','-z','in')]}
        if k=='gearbox':
            dp=drv.get('ports',[])
            if dp==['-z','+z'] : m['gearbox']=[('Input shaft','-z','in'),('Output shaft','+z','out')]
            elif dp==['-z','-y']: m['gearbox']=[('Input shaft','-z','in'),('Output shaft','-y','out')]
            elif dp==['-z','+z','-y']: m['gearbox']=[('Input shaft','-z','in'),('Output shaft','+z','out'),('Branch shaft','-y','out')]
            else: raise Exception('gearbox '+f+str(dp))
        for n,sk,dr in m[k]: ps.append(shaft(d,n,sk,dr))
    else: return None
    return ps
changed=[]
for f in sorted(glob.glob(ROOT+'/**/*.json',recursive=True)):
    t=open(f).read()
    try:d=json.loads(t)
    except: continue
    if not isinstance(d,dict) or 'role' not in d: continue
    if d['role'] not in ('engine','fueltank','battery','alternator','transmission','differential','transfer'): continue
    ps=build(d,f)
    if ps is None: continue
    if json.dumps(d,indent=2,ensure_ascii=False)+'\n'!=t: print('NO ROUNDTRIP',f); continue
    if 'ports' in d: continue
    for p in ps:
        kind=p['type']; pt=p.get('at') or sock(d,p['socket'])['at']; face=p.get('face') or sock(d,p['socket'])['dir']
        d['boxes'].append(stub(d,pt,face,kind))
    assert len(d['boxes'])<=16,f
    # place ports after sockets
    out={}
    for k,v in d.items():
        out[k]=v
        if k=='sockets': out['ports']=ps
    open(f,'w').write(json.dumps(out,indent=2,ensure_ascii=False)+'\n')
    changed.append(f)
print(len(changed))
