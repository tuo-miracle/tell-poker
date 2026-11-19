# 每個鏡頭：切段 → 轉 CFR 30 → vidstab 穩定 →（可選）變速。輸出影像給 Remotion、音訊給混音
import subprocess,json,os,sys
sys.path.insert(0,os.path.dirname(__file__))
from edl import S
D=os.path.dirname(os.path.abspath(__file__))
# 頭盔錄影（/sdcard/Oculus/VideoShots/ 原檔名，未改名、未轉檔）放的資料夾；預設 <本資料夾>/src
SRCDIR=os.environ.get("TELL_SRC",D+"/src")
V=lambda d,t:f"{SRCDIR}/com.maxlai.tellpoker-{d}-{t}-0.mp4"
SRC={"234540":V("20261104","234540"),"190120":V("20261105","190120"),"003317":V("20261104","003317"),"231222":V("20261103","231222"),"230425":V("20261103","230425"),"224236":V("20261103","224236"),"093206":V("20261105","093206")}
SH=D+"/shots";PUB=D+"/remotion/public";os.makedirs(PUB,exist_ok=True);os.makedirs(SH,exist_ok=True)
STAB=os.environ.get("STAB","shakiness=5:accuracy=15|smoothing=10:zoom=5:optzoom=0:interpol=bicubic:crop=keep")
def run(*a): subprocess.run(a,check=True,stderr=subprocess.PIPE)
only=set(sys.argv[1:])
man=[]
for s in S:
    if s.get("end") or s.get("card"): man.append(dict(s,frames=int(s["dur"]*30)));continue
    sp=s.get("speed",1.0);dur=s["b"]-s["a"];out=dur/sp;hold=s.get("hold",0)
    mp4=f"{PUB}/s{s['id']}.mp4";wav=f"{SH}/s{s['id']}.wav";raw=f"{SH}/r{s['id']}.mp4";trf=f"{SH}/t{s['id']}.trf"
    if not only or s['id'] in only:
        run("ffmpeg","-v","error","-y","-ss",str(s["a"]-0.5),"-t",str(dur+1.0),"-i",SRC[s["src"]],"-fps_mode","passthrough","-vf","format=yuv420p","-c:v","libx264","-crf","12","-preset","fast","-an",raw)
        det,tr=STAB.split("|")
        run("ffmpeg","-v","error","-y","-i",raw,"-vf",f"vidstabdetect={det}:result={trf}","-f","null","-")
        vf=f"vidstabtransform=input={trf}:{tr},unsharp=5:5:0.4:5:5:0,trim=start=0.5:duration={dur:.3f},setpts=(PTS-STARTPTS)/{sp},fps=30"+(f",tpad=stop_mode=clone:stop_duration={hold}" if hold else "")
        run("ffmpeg","-v","error","-y","-i",raw,"-vf",vf,"-fps_mode","cfr","-c:v","libx264","-crf","14","-preset","medium","-pix_fmt","yuv420p","-g","15","-an",mp4)
        run("ffmpeg","-v","error","-y","-ss",str(s["a"]),"-t",str(out+hold),"-i",SRC[s["src"]],"-vn","-ac","2","-ar","48000",wav)
    frames=round((out+hold)*30)
    man.append(dict(s,frames=frames))
json.dump(man,open(D+"/remotion/src/edl.json","w"),indent=1,ensure_ascii=False)
print("total",sum(m["frames"] for m in man)/30)
