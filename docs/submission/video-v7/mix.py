# 混音：遊戲原聲接起來（每段 20ms 淡入淡出）、原創配樂（對白時壓低）、整體 -16 LUFS
import json,subprocess,os,sys
import numpy as np
D=os.path.dirname(os.path.abspath(__file__));SR=48000;FPS=30
man=json.load(open(D+"/remotion/src/edl.json"))
def rd(p):
    raw=subprocess.run(["ffmpeg","-v","error","-i",p,"-f","f32le","-ac","2","-ar",str(SR),"-"],capture_output=True).stdout
    return np.frombuffer(raw,dtype=np.float32).reshape(-1,2).copy()
total=sum(m["frames"] for m in man)/FPS;N=int(total*SR)+SR
game=np.zeros((N,2));duck=np.zeros(N);t=0.0;marks={}
for m in man:
    marks[m["id"]]=t;n=int(m["frames"]/FPS*SR)
    if not m.get("end") and not m.get("mute"):
        a=rd(f"{D}/shots/s{m['id']}.wav")[:n]*10**(m.get("gain",0)/20);k=min(960,len(a)//2);ramp=np.linspace(0,1,k)[:,None]
        a[:k]*=ramp;a[-k:]*=ramp[::-1];game[int(t*SR):int(t*SR)+len(a)]+=a
    for d in m.get("dlg",[]):
        duck[int((t+d[2]-0.12)*SR):int((t+d[3]+0.1)*SR)]=1
    t+=m["frames"]/FPS
end=t;total_s=t
# 配樂：80 BPM，A 小調。原創合成，無第三方素材
beat=60/80;tt=np.arange(N)/SR;rng=np.random.default_rng(7)
def tone(f,dur,h=(1,.45,.2,.08),dec=2.2):
    x=np.arange(int(dur*SR))/SR
    s=sum(a*np.sin(2*np.pi*f*(i+1)*x) for i,a in enumerate(h))
    return s*np.exp(-x*dec)*np.minimum(1,x/.008)
def put(b,x,at,g):
    i=int(at*SR);e=min(len(b),i+len(x))
    if i<len(b): b[i:e]+=g*x[:e-i]
sub=np.zeros(N);pulse=np.zeros(N);keys=np.zeros(N);hit=np.zeros(N);rise=np.zeros(N)
def kick(g=1.):
    x=np.arange(int(.32*SR))/SR;return g*np.sin(2*np.pi*(48*x+18*(1-np.exp(-x*28))/28*2))*np.exp(-x*11)
sec=lambda i:marks[i]
tl=sec("03");story=sec("06");read=sec("02");lie=sec("11");run=sec("12");pay=sec("15");you=sec("17");talk=sec("19");allin=sec("21");win=sec("22");but=sec("24");card=sec("25")
def lvl(x):  # 分段能量
    pts=[(0,.42),(tl,.55),(story,.45),(read,.6),(lie,.5),(run,1.0),(pay,.35),(you,.5),(talk,.45),(allin,.95),(win,1.0),(but,0.0),(card,.8),(total_s,.6)]
    return np.interp(x,[p[0] for p in pts],[p[1] for p in pts])
# 心跳式脈動（雙擊）貫穿；扣牌段與 all-in 更密
b=0.0
while b<but:
    g=lvl(b)
    put(pulse,kick(1),b,g);put(pulse,kick(.6),b+beat*.33,g*.8)
    if lie<=b<pay or allin<=b<but: put(pulse,kick(.5),b+beat*.5,g*.55)
    b+=beat
# 和弦：Am F Dm E，每兩小節
ch=[(110,130.81,164.81),(87.31,130.81,174.61),(73.42,146.83,174.61),(82.41,123.47,207.65)]
b=tl;i=0
while b<but:
    if not (pay-.1<b<you-.5):
        for f in ch[i%4]: put(keys,tone(f*2,beat*6,dec=.9),b,.11*lvl(b)+.03)
    put(sub,tone(ch[i%4][0]/2,beat*8,h=(1,.15),dec=.35),b,.5*lvl(b))
    b+=beat*8;i+=1
# 高音點綴：只在升溫段
b=read;notes=[659.25,587.33,523.25,440.0]
j=0
while b<but:
    if lvl(b)>.45 and not (pay-.1<b<you-.5): put(keys,tone(notes[j%4],1.4,h=(1,.2),dec=3.2),b,.045)
    b+=beat*2;j+=1
# 重音：標題、揭曉、結尾。上升音：跑牌段
for at,g in [(tl+.2,1.),(pay+.05,.8),(win+4.5,.9),(card+.1,1.)]:
    x=np.arange(int(1.6*SR))/SR;put(hit,np.sin(2*np.pi*(38*x+22*(1-np.exp(-x*20))/20))*np.exp(-x*3.0),at,g)
d=int((pay-sec("13"))*SR);ri=int(sec("13")*SR);n=rng.standard_normal(d);n=np.convolve(n,np.ones(14)/14,"same")
rise[ri:ri+d]=n*np.linspace(0,1,d)**2.5*.28
# 結尾和弦
for f in (110,164.81,220,261.63,329.63): put(keys,tone(f,5.5,dec=.55),card+.1,.16)
mus=(sub+.8*pulse+keys+.55*hit+rise)
env=np.zeros(N);cur=0.
atk=np.exp(-1/(SR*4/FPS));rel=np.exp(-1/(SR*25/FPS))  # 對白起點 4 格壓下、結束 25 格回復
for i in range(N):
    c=atk if duck[i]>cur else rel
    cur=duck[i]+(cur-duck[i])*c;env[i]=cur
gain=10**(-11*env/20)*.34
fade=np.ones(N);e=int(total_s*SR);fade[e-int(2.5*SR):e]=np.linspace(1,0,int(2.5*SR));fade[e:]=0
st=np.stack([mus*gain*fade,mus*gain*fade],1)
# 立體聲微寬
st[:,1]=np.roll(st[:,1],int(.007*SR))
g2=game*1.0
mix=g2+st;mix=mix[:int(total_s*SR)]
tmp=D+"/out/mix_raw.wav"
mix.astype(np.float32).tofile(tmp+".f32")
subprocess.run(["ffmpeg","-v","error","-y","-f","f32le","-ar",str(SR),"-ac","2","-i",tmp+".f32","-af","loudnorm=I=-15:TP=-1.5:LRA=11,alimiter=limit=0.84:level=false","-ar",str(SR),"-c:a","pcm_s16le",D+"/out/mix.wav"],check=True)
os.remove(tmp+".f32");print("mix ok",round(total_s,1))
