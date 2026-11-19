# 混音：遊戲原聲接起來（每段 20ms 淡入淡出）、原創配樂（對白時壓低）、整體 -16 LUFS
import json,subprocess,os,sys
import numpy as np
D=os.path.dirname(os.path.abspath(__file__));SR=48000;FPS=30
os.makedirs(D+"/out",exist_ok=True)
man=json.load(open(D+"/remotion/src/edl.json"))
def rd(p,af=None):
    raw=subprocess.run(["ffmpeg","-v","error","-i",p]+(["-af",af] if af else [])+["-f","f32le","-ac","2","-ar",str(SR),"-"],capture_output=True).stdout
    return np.frombuffer(raw,dtype=np.float32).reshape(-1,2).copy()
total=sum(m["frames"] for m in man)/FPS;N=int(total*SR)+SR
# 遊戲原聲先降噪再過靜音門：沒人講話、沒音效時不留底噪
CLEAN="afftdn=nr=18:nf=-48,agate=threshold=0.012:ratio=12:attack=4:release=220:range=0.003"
game=np.zeros((N,2));duck=np.zeros(N);t=0.0;marks={}
for m in man:
    marks[m["id"]]=t;n=int(m["frames"]/FPS*SR)
    if not m.get("end") and not m.get("card") and not m.get("mute"):
        a=rd(f"{D}/shots/s{m['id']}.wav",CLEAN)[:n]*10**(m.get("gain",0)/20);k=min(960,len(a)//2);ramp=np.linspace(0,1,k)[:,None]
        for f,e,db in m.get("trim",[]):
            w=np.ones(len(a));w[int(f*SR):int(e*SR)]=10**(db/20);a*=np.convolve(w,np.ones(960)/960,"same")[:,None]
        a[:k]*=ramp;a[-k:]*=ramp[::-1];game[int(t*SR):int(t*SR)+len(a)]+=a
    for d in m.get("dlg",[]):
        duck[int((t+d[2]-0.12)*SR):int((t+d[3]+0.1)*SR)]=1
    t+=m["frames"]/FPS
end=t;total_s=t
# 配樂：現成授權曲，由 TRACK 環境變數選；標準化到固定響度才不會蓋掉對白
rng=np.random.default_rng(7)
TRACK,OFFSET=os.environ.get("TRACK","Hep_Cats"),float(os.environ.get("OFFSET","0"))
tr=rd(f"{D}/music/{TRACK}.mp3").mean(1)[int(OFFSET*SR):][:N]
tr=tr/np.sqrt((tr**2).mean())*10**(float(os.environ.get("MUSICDB","-33"))/20)
music=np.zeros(N);music[:len(tr)]=tr*(0 if os.environ.get("NOMUSIC") else 1)
if len(tr)<N: raise SystemExit("track too short")
def tone(f,dur,h=(1,.45,.2,.08),dec=2.2):
    x=np.arange(int(dur*SR))/SR
    s=sum(a*np.sin(2*np.pi*f*(i+1)*x) for i,a in enumerate(h))
    return s*np.exp(-x*dec)*np.minimum(1,x/.008)
def put(b,x,at,g):
    i=int(at*SR);e=min(len(b),i+len(x))
    if i<len(b): b[i:e]+=g*x[:e-i]
hit=np.zeros(N);sec=lambda i:marks[i]
tl=sec("03");card=sec("25")
# 動畫音效：轉場掃風、章節重音、標籤輕響、翻牌聲、快慢計時滴答、揭曉牌提示。都比對應畫面早 2–3 格
sfx=np.zeros(N)
def noise_burst(dur,lo,dec):
    n=rng.standard_normal(int(dur*SR));n=n-np.convolve(n,np.ones(lo)/lo,"same");x=np.arange(len(n))/SR
    return n*np.exp(-x*dec)*np.minimum(1,x/.002)
def whoosh(dur=.5):
    n=rng.standard_normal(int(dur*SR));s=np.linspace(0,1,len(n));w=np.sin(np.pi*s)**2
    lp=np.convolve(n,np.ones(48)/48,"same");return lp*w/np.sqrt((lp**2).mean())*.12
click=noise_burst(.03,4,160)*.5+tone(2100,.03,h=(1,),dec=120)*.35
flip=noise_burst(.07,6,55)*.8
tick=tone(1500,.03,h=(1,.3),dec=150)*.5
ping=tone(1318.5,.7,h=(1,.3),dec=5)*.3
ids=[m["id"] for m in man]
for a,c in zip(man,man[1:]):
    if a.get("card") or c.get("card") or a["id"]=="03" or c.get("end"): put(sfx,whoosh(),marks[c["id"]]-.25,.9)
for m in man:
    at=marks[m["id"]]
    if m.get("card"):
        x=np.arange(int(1.4*SR))/SR;put(hit,np.sin(2*np.pi*(40*x+22*(1-np.exp(-x*20))/20))*np.exp(-x*3.4),at+.2,.75)
        # 和字卡動畫同步：發牌、翻牌；All in 是籌碼落下和推到一起
        if m["id"]=="c4":
            for i in range(12): put(sfx,tick,at+.15+i*.09+.28,.55)
            put(sfx,whoosh(.35),at+1.9,.5)
        else:
            for i in range(5): put(sfx,flip,at+.1+i*.13,.55)
            for f in {"c1":[1.5],"c2":[.9,2.0],"c3":[1.0,1.35,1.7]}[m["id"]]: put(sfx,flip,at+f,.8)
    if m.get("tag"): put(sfx,click,at+m["tag"][1]-.07,.8)
    if m.get("flash"): put(sfx,whoosh(.28),at-.2,.6)
    if m.get("meter")=="fast":
        for i in range(5): put(sfx,tick,at+m["story"][1]+i*.1,.8)
    if m.get("meter")=="slow":
        for i in range(6): put(sfx,tick,at+m["story"][1]+i*.55,.8)
    if m["id"] in ("08","18"):
        tk=json.load(open(D+"/remotion/src/tracks.json"))[m["id"]]["pts"];f=next(i for i,p in enumerate(tk) if p[2]);put(sfx,ping,at+f/FPS+.03,.8)
for i in range(4): put(sfx,flip,tl+(6+i*6)/FPS-.07,.7);put(sfx,flip,card+(6+i*6)/FPS-.07,.7)
for at,g in [(tl+.2,1.),(card+.1,1.)]+[(marks[m["id"]]+.05,.7) for m in man if m.get("flash")]:
    x=np.arange(int(1.6*SR))/SR;put(hit,np.sin(2*np.pi*(38*x+22*(1-np.exp(-x*20))/20))*np.exp(-x*3.0),at,g)
mus=music
env=np.zeros(N);cur=0.
atk=np.exp(-1/(SR*4/FPS));rel=np.exp(-1/(SR*25/FPS))  # 對白起點 4 格壓下、結束 25 格回復
for i in range(N):
    c=atk if duck[i]>cur else rel
    cur=duck[i]+(cur-duck[i])*c;env[i]=cur
gain=10**(-6*env/20)
fade=np.ones(N);e=int(total_s*SR);fade[e-int(2.5*SR):e]=np.linspace(1,0,int(2.5*SR));fade[e:]=0
sfxg=10**(-6*env/20)*.3
hg=.2*hit*10**(-6*env/20)
st=np.stack([(mus*gain+sfx*sfxg+hg)*fade]*2,1)
g2=game*1.0
mix=g2+st;mix=mix[:int(total_s*SR)]
tmp=D+"/out/mix_raw.wav"
mix.astype(np.float32).tofile(tmp+".f32")
import re
TARGET=float(os.environ.get("LUFS","-16"))
log=subprocess.run(["ffmpeg","-hide_banner","-f","f32le","-ar",str(SR),"-ac","2","-i",tmp+".f32","-af","ebur128","-f","null","-"],capture_output=True,text=True).stderr
I=float(re.findall(r"I:\s+(-?[\d.]+) LUFS",log)[-1]);print("raw LUFS",I)
subprocess.run(["ffmpeg","-v","error","-y","-f","f32le","-ar",str(SR),"-ac","2","-i",tmp+".f32","-af",f"volume={TARGET-I:.2f}dB,alimiter=limit=0.84:level=false","-ar",str(SR),"-c:a","pcm_s16le",D+"/out/"+os.environ.get("OUT","mix.wav")],check=True)
os.remove(tmp+".f32");print("mix ok",round(total_s,1))
