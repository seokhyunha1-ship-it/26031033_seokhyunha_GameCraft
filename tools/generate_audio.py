"""Original procedural audio. Python standard library only; no borrowed samples."""
from pathlib import Path
import wave,math,array,random
out=Path(__file__).resolve().parents[1]/"resource/resource/sounds"
sr=22050
def save(name,buf):
 pcm=array.array('h',(int(max(-.95,min(.95,v))*32767) for v in buf))
 with wave.open(str(out/(name+'.wav')),'wb') as w:
  w.setnchannels(1);w.setsampwidth(2);w.setframerate(sr);w.writeframes(pcm.tobytes())
for name,bpm,rootnote in [('title',96,60),('early',120,60),('middle',132,65),('space',84,57)]:
 beat=60/bpm;duration=beat*32;n=int(sr*duration);buf=[0.0]*n
 def note(start,length,midi,amp):
  freq=440*2**((midi-69)/12)
  for j in range(int(length*sr)):
   t=j/sr;env=min(1,t/.015)*min(1,(length-t)/.08)*math.exp(-t*2)
   buf[(int(start*sr)+j)%n]+=amp*env*(math.sin(2*math.pi*freq*t)+.24*math.sin(4*math.pi*freq*t))
 for k in range(32):
  chord=[0,5,9,7][(k//8)%4]
  note(k*beat,beat*.85,rootnote+chord+[0,4,7,12,7,4,2,7][k%8]+12,.16)
  if k%2==0:note(k*beat,beat*1.8,rootnote+chord-12,.20)
  if k%4==0:
   for step in [0,4,7]:note(k*beat,beat*3.8,rootnote+chord+step,.07)
 save('bgm_'+name,buf)
for name,f0,f1,duration in [('eatfood',660,990,.18),('gameover',330,90,.8),('gamestart',440,880,.5),('jump',240,780,.23),('jumpholding',180,360,.35),('menubuttonclick',660,880,.12),('menuclick',400,500,.06),('splash',650,100,.2),('swim',180,280,.15),('watercannon',800,160,.3),('parry',880,1320,.22),('hit',160,60,.15),('fire',220,100,.12)]:
 rng=random.Random(42);buf=[]
 for i in range(int(sr*duration)):
  t=i/sr;u=t/duration;env=math.sin(math.pi*u)**2*(1-u)
  tone=math.sin(2*math.pi*(f0*t+(f1-f0)*t*t/(2*duration)))
  if name in ['splash','watercannon','fire']:tone=.5*tone+.5*rng.uniform(-1,1)
  buf.append(.5*env*tone)
 save('sfx_'+name,buf)

# Two short warning beeps for rejected same-color water contact.
buf=[]
for i in range(int(sr*.28)):
 t=i/sr
 local=t if t<.10 else t-.16
 active=(0<=local<.10)
 env=math.sin(math.pi*local/.10)**2 if active else 0
 buf.append(.45*env*math.sin(2*math.pi*1050*t))
save('sfx_reject',buf)
