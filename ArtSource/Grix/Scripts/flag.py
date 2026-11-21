from PIL import Image, ImageDraw
import math
S=1024
RED=(200,16,46); WHITE=(246,246,246); BLUE=(23,23,150); DARK=(14,8,14)
im=Image.new('RGB',(S,S),WHITE); d=ImageDraw.Draw(im)
# Croatian-inspired horizontal tricolour
d.rectangle([0,0,S,S/3],fill=RED); d.rectangle([0,S/3,S,2*S/3],fill=WHITE); d.rectangle([0,2*S/3,S,S],fill=BLUE)
def hexpts(cx,cy,r,rot=30):
    return [(cx+r*math.cos(math.radians(60*i+rot)),cy+r*math.sin(math.radians(60*i+rot))) for i in range(6)]
cx,cy=S/2,S/2
# shield: big hex outline, filled with a red/white chequer of small hexes (honeycomb nod to the sahovnica)
R=330
d.polygon(hexpts(cx,cy,R+34),fill=DARK)
d.polygon(hexpts(cx,cy,R+14),fill=BLUE)
d.polygon(hexpts(cx,cy,R),fill=WHITE)
r=58; w=r*math.sqrt(3)
mask=Image.new('L',(S,S),0); ImageDraw.Draw(mask).polygon(hexpts(cx,cy,R-6),fill=255)
cells=Image.new('RGB',(S,S),WHITE); cd=ImageDraw.Draw(cells)
for row in range(-8,9):
    for col in range(-8,9):
        x=cx+col*w+(row%2)*w/2; y=cy+row*r*1.5
        c=RED if (col+row//1+ (row%2)*0) %2==0 else WHITE
        c=RED if ((col*2+(row%2))//1)%4 in (0,1) and False else c
        cd.polygon(hexpts(x,y,r-5),fill=c)
        cd.polygon(hexpts(x,y,r-5),outline=DARK,width=4)
im.paste(cells,(0,0),mask)
im.save('tex/Grix_Flag.png')
