# 壓窗簾和燈的高光：只動畫面上半部的亮部；泡泡、襯衫這類實心白塊不動
import cv2,numpy as np,subprocess,sys
def soften(img,knee=165,slope=0.35,top=0.6,protect=True):
    h,w=img.shape[:2]
    wy=np.clip((top-np.arange(h)/h)/(top*0.4),0,1)[:,None].astype(np.float32)
    m=np.repeat(wy,w,axis=1)
    if protect:
        s=cv2.resize(img,(480,270),interpolation=cv2.INTER_AREA);g=cv2.cvtColor(s,cv2.COLOR_BGR2GRAY)
        b=(g>225).astype(np.uint8)
        b=cv2.morphologyEx(b,cv2.MORPH_CLOSE,np.ones((5,5),np.uint8))
        solid=cv2.morphologyEx(b,cv2.MORPH_OPEN,np.ones((13,13),np.uint8))   # 細條會被去掉，實心塊留下
        solid=cv2.GaussianBlur(cv2.dilate(solid,np.ones((5,5),np.uint8)).astype(np.float32),(0,0),1.5)
        m=m*(1-np.clip(cv2.resize(solid,(w,h)),0,1))
    f=img.astype(np.float32);c=np.where(f>knee,knee+(f-knee)*slope,f)
    return np.clip(f*(1-m[...,None])+c*m[...,None],0,255).astype(np.uint8)
def run(path,protect):
    c=cv2.VideoCapture(path);w,h=int(c.get(3)),int(c.get(4));tmp=path+".tmp.mp4"
    p=subprocess.Popen(["ffmpeg","-v","error","-y","-f","rawvideo","-pix_fmt","bgr24","-s",f"{w}x{h}","-r","30","-i","-","-c:v","libx264","-crf","14","-preset","medium","-pix_fmt","yuv420p","-g","15","-an",tmp],stdin=subprocess.PIPE)
    while True:
        ok,f=c.read()
        if not ok: break
        p.stdin.write(soften(f,protect=protect).tobytes())
    p.stdin.close();p.wait();subprocess.run(["mv",tmp,path],check=True)
if __name__=="__main__":
    for a in sys.argv[1:]:
        k,_,mode=a.partition(":");run(k,mode!="noprotect")
