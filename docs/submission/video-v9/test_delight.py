import numpy as np,cv2,sys,os
sys.path.insert(0,os.path.dirname(__file__))
from delight import soften
H,W=270,480
img=np.full((H,W,3),(110,140,160),np.uint8)
for y in range(10,110,10): img[y:y+4,20:200]=(250,250,250)       # 窗簾細白條
img[20:80,300:440]=(250,250,250);cv2.putText(img,"Pot",(320,55),cv2.FONT_HERSHEY_SIMPLEX,.7,(0,0,0),2)  # 泡泡
img[200:260,180:300]=(245,245,245)                                # 下方襯衫
o=soften(img)
assert o[12,100].mean()<215,"blind slat not dimmed"
assert o[30,420].mean()>240,"bubble dimmed"
assert o[230,240].mean()>240,"shirt dimmed"
assert abs(int(o[150,100,0])-110)<2,"midtone changed"
o2=soften(img,protect=False);assert o2[30,420].mean()<215,"protect flag ignored"
print("ok")
