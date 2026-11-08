"""Original procedural audio. Python standard library only; no borrowed samples."""
from pathlib import Path
import wave,math,array,random,argparse
parser=argparse.ArgumentParser()
parser.add_argument("--only", nargs="*", help="Generate only these sound stems")
only=parser.parse_args().only
out=Path(__file__).resolve().parents[1]/"resource/resource/sounds"
sr=22050
def save(name,buf):
 if only is not None and name not in only:return
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

# A clear metallic success chime, with an immediate strike and a short rising tail.
duration=.36
rng=random.Random(90)
buf=[]
for i in range(int(sr*duration)):
 t=i/sr
 attack=min(1,t/.002)
 tail=min(1,(duration-t)/.03)
 strike=.40*math.sin(2*math.pi*1320*t)*math.exp(-t*17)
 bright=.20*math.sin(2*math.pi*1980*t)*math.exp(-t*20)
 local=max(0,t-.045)
 chime=.24*math.sin(2*math.pi*1760*local)*math.exp(-local*13) if t>=.045 else 0
 click=.10*rng.uniform(-1,1)*math.exp(-t*140)
 buf.append(attack*tail*(strike+bright+chime+click))
save('sfx_parry',buf)

# A dry jaw-clack above a short low thump; no borrowed audio samples.
duration=.22
rng=random.Random(91)
buf=[]
for i in range(int(sr*duration)):
 t=i/sr
 attack=min(1,t/.0015)
 tail=min(1,(duration-t)/.025)
 thump=.48*math.sin(2*math.pi*(105*t-150*t*t))*math.exp(-t*22)
 clack=.25*rng.uniform(-1,1)*math.exp(-t*100)
 wood=.18*math.sin(2*math.pi*720*t)*math.exp(-t*65)
 buf.append(attack*tail*(thump+clack+wood))
save('sfx_bite',buf)
