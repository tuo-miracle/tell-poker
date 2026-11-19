# 每個鏡頭：切段 → 轉 CFR 30 → vidstab 穩定 →（可選）變速。輸出影像給 Remotion、音訊給混音
import subprocess,json,os,sys
sys.path.insert(0,os.path.dirname(__file__))
from edl import S
D=os.path.dirname(os.path.abspath(__file__));B=os.path.dirname(D)
SRC={"234540":D+"/src/v234540.mp4","003317":B+"/clips/v003317.mp4","231222":B+"/clips/v231222.mp4","230425":B+"/clips/v230425.mp4","224236":B+"/clips/v2.mp4"}
SH=D+"/shots";PUB=D+"/remotion/public";os.makedirs(PUB,exist_ok=True)
STAB=os.environ.get("STAB","shakiness=5:accuracy=15|smoothing=10:zoom=5:optzoom=0:interpol=bicubic:crop=keep")
def run(*a): subprocess.run(a,check=True,stderr=subprocess.PIPE)
only=set(sys.argv[1:])
man=[]
for s in S:
    if s.get("end"): man.append(dict(s,frames=int(s["dur"]*30)));continue
    sp=s.get("speed",1.0);dur=s["b"]-s["a"];out=dur/sp
    mp4=f"{PUB}/s{s['id']}.mp4";wav=f"{SH}/s{s['id']}.wav";raw=f"{SH}/r{s['id']}.mp4";trf=f"{SH}/t{s['id']}.trf"
    if not only or s['id'] in only:
        run("ffmpeg","-v","error","-y","-ss",str(s["a"]-0.5),"-t",str(dur+1.0),"-i",SRC[s["src"]],"-fps_mode","passthrough","-vf","format=yuv420p","-c:v","libx264","-crf","12","-preset","fast","-an",raw)
        det,tr=STAB.split("|")
        run("ffmpeg","-v","error","-y","-i",raw,"-vf",f"vidstabdetect={det}:result={trf}","-f","null","-")
        vf=f"vidstabtransform=input={trf}:{tr},unsharp=5:5:0.4:5:5:0,trim=start=0.5:duration={dur:.3f},setpts=(PTS-STARTPTS)/{sp},fps=30"
        run("ffmpeg","-v","error","-y","-i",raw,"-vf",vf,"-fps_mode","cfr","-c:v","libx264","-crf","14","-preset","medium","-pix_fmt","yuv420p","-g","15","-an",mp4)
        run("ffmpeg","-v","error","-y","-ss",str(s["a"]),"-t",str(dur),"-i",SRC[s["src"]],"-vn","-ac","2","-ar","48000",wav)
    frames=round(out*30)
    man.append(dict(s,frames=frames))
json.dump(man,open(D+"/remotion/src/edl.json","w"),indent=1,ensure_ascii=False)
print("total",sum(m["frames"] for m in man)/30)
