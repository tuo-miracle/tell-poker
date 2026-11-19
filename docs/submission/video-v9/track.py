# 追蹤遊戲內揭曉牌在鏡頭中的位置，讓框線黏在牌上。輸出每格中心（百分比）與可見與否
import cv2,json,sys,os
import numpy as np
D=sys.argv[1] if len(sys.argv)>1 else os.path.dirname(os.path.abspath(__file__))  # 專案根目錄（含 remotion/）
# id: (參考秒數, 1920 寬座標的 x0,y0,x1,y1)
T={"08":(2.5,1020,810,1629,915),"18":(2.2,414,870,675,969)}
out={}
for k,(t0,x0,y0,x1,y1) in T.items():
    cap=cv2.VideoCapture(f"{D}/remotion/public/s{k}.mp4");fr=[]
    while True:
        ok,f=cap.read()
        if not ok: break
        fr.append(cv2.cvtColor(f,cv2.COLOR_BGR2GRAY))
    h,w=fr[0].shape;s=w/1920
    i0=min(int(t0*30),len(fr)-1);tpl=fr[i0][int(y0*s):int(y1*s),int(x0*s):int(x1*s)]
    # 從參考格往前後逐格追，模板跟著更新，才撐得住頭部轉動；分數掉下去就當牌還沒出現或已消失
    n=len(fr);a=np.zeros((n,3));tw,th=tpl.shape[1],tpl.shape[0]
    def walk(order):
        t=tpl;px,py=int(x0*s),int(y0*s)
        for i in order:
            g=fr[i];m=60;X0,Y0=max(0,px-m),max(0,py-m);win=g[Y0:min(h,py+th+m),X0:min(w,px+tw+m)]
            if win.shape[0]<th or win.shape[1]<tw: break
            r=cv2.matchTemplate(win,t,cv2.TM_CCOEFF_NORMED);_,sc,_,loc=cv2.minMaxLoc(r)
            if sc<0.6: break
            px,py=X0+loc[0],Y0+loc[1];a[i]=[px+tw/2,py+th/2,sc];t=g[py:py+th,px:px+tw]
    walk(range(i0,n));walk(range(i0,-1,-1))
    vis=a[:,2]>0
    for c in (0,1):
        v=np.interp(np.arange(n),np.where(vis)[0],a[vis,c]);a[:,c]=np.convolve(np.pad(v,3,mode="edge"),np.ones(7)/7,"valid")
    out[k]=dict(w=(x1-x0)/19.2,h=(y1-y0)/10.8,pts=[[round(p[0]/w*100,2),round(p[1]/h*100,2),int(u)] for p,u in zip(a.tolist(),vis)])
    print(k,w,h,"frames",len(fr),"visible",int(vis.sum()),"first",int(np.argmax(vis)),"score median",round(float(np.median(a[vis,2])),2))
json.dump(out,open(f"{D}/remotion/src/tracks.json","w"))
