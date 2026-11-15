# 第 09-1 章：產生第十一批工作清單 assets/GPT_第十一批_工作清單.csv（python3 tools/batch11_list.py）
import csv, itertools
T=16; UP=8
rows=[]
def add(group,name,desc,w,h,unit='tile',frames=1,note=''):
    pw,ph=(w*T,h*T) if unit=='tile' else (w,h)
    up=next(k for k in (8,4,2,1) if pw*frames*k<=1536 and ph*k<=1536)
    rows.append([group,f'b11_{name}.png',desc,pw*frames,ph,frames,pw*frames*up,ph*up,note])
# A
for b,zh in [('temperate','溫帶草地'),('meadow','草甸'),('steppe','草原（乾）')]:
    for i in range(1,5): add('A',f'g_grass_{b}_{i}',zh,1,1,note='1、2 款幾乎素面；3、4 款帶少量草叢或小花；不透明、四邊無縫')
for i in range(1,4): add('A',f'g_forest_floor_{i}','林地地面（落葉與苔）',1,1,note='不透明、四邊無縫')
for i in range(1,4): add('A',f'g_hill_{i}','丘陵地面（碎石與短草）',1,1,note='不透明、四邊無縫')
for i in range(1,3): add('A',f'g_sand_{i}','沙灘',1,1,note='不透明、四邊無縫')
add('A','g_sand_wet_1','濕沙',1,1,note='不透明、四邊無縫')
for k,zh in [('shallow','淺水'),('river','河'),('sea','海'),('deep','深海')]:
    for i in range(1,3): add('A',f'g_water_{k}_{i}',zh,1,1,note='只有波紋、不畫雜點；不透明、四邊無縫')
add('A','g_ash_1','焦土（野火燒過）',1,1,note='不透明、四邊無縫')
for k,zh,n in [('grass_sand','草↔沙',''),('sand_shallow','沙↔淺水','水岸一圈淺色浪花'),('shallow_deep','淺水↔深水',''),('grass_dirt','草↔泥土',''),
               ('grass_stone','草↔石頭路','路緣一排邊石'),('grass_brick','草↔磚地',''),('grass_field','草↔田','田四周是田埂'),('grass_forest','草↔林地',''),('grass_hill','草↔丘陵','')]:
    add('A',f'w_{k}',f'過渡 {zh}（Wang 2-corner 16 格，4×4 排，順序照 wang16.png；位元為 1 的角是「↔」右邊那種）',4,4,note=n)
# B
for k,zh,n in [('road_dirt','泥土路','兩條車轍，橫豎通用'),('road_stone','石頭路','不規則石塊'),('road_brick','磚路','人字形'),('yard','院子','踩實的土加少量雜草'),('garden','菜畦','橫向一壟一壟、綠色作物')]:
    for i in range(1,3): add('B',f'g_{k}_{i}',zh,1,1,note=n+'；不透明、四邊無縫')
add('B','g_plaza_dirt_1','夯土廣場',1,1,note='不透明、四邊無縫')
for i in range(1,3): add('B',f'g_plaza_stone_{i}','石板廣場（大塊方石）',1,1,note='不透明、四邊無縫')
for k,zh in [('wheat','成熟麥田'),('green','青麥'),('vegetable','菜田'),('flax','亞麻（藍花）'),('plowed','翻土'),('sown','播種'),('sprout','綠苗'),('fallow','休耕')]:
    add('B',f'g_field_{k}',zh,1,1,note='畦溝橫向；不透明、四邊無縫')
add('B','g_cobble_1','城內石子地（比石頭路細碎）',1,1,note='不透明、四邊無縫')
# C
C=[('tent_{i}','帳篷',2,2,2,1),('bonfire','篝火',2,2,1,4),('stockpile','囤糧堆',2,2,1,1),('hut_{i}','木屋',3,3,3,1),('farmhouse_{i}','農舍（帶小穀倉）',4,4,2,1),
   ('hall_village','村會所',5,5,1,1),('shrine','小祠',2,3,1,1),('granary','穀倉',3,4,1,1),('well','水井',2,2,1,1),('hunting_lodge','獵人小屋',4,3,1,1),('fishing_hut','漁夫高腳屋',3,3,1,1),
   ('house_{i}','城鎮住屋（半木造）',4,4,3,1),('stone_house_1','石屋',4,5,1,1),('stone_house_2','石屋',4,5,1,1),('stone_house_3','石屋（三層）',4,6,1,1),('hall_town','鎮會所',7,7,1,1),
   ('stall_{i}','市集攤（單攤，程式排成一列）',2,2,3,1),('smithy','鐵匠鋪',5,4,1,1),('workshop','木工坊',5,4,1,1),('kiln','窯',3,3,1,1),('windmill','風車（葉片轉動）',4,7,1,4),
   ('inn','旅店',5,5,1,1),('warehouse','倉庫',5,4,1,1),('stable','馬廄',5,3,1,1),
   ('brick_house_{i}','磚屋',4,5,2,1),('temple','神殿',8,10,1,1),('bell_tower','鐘樓',2,6,1,1),('school','學校',6,6,1,1),('scriptorium','抄經房',6,5,1,1),('barracks','兵營',7,5,1,1),
   ('archery','射箭場',6,4,1,1),('watchtower','瞭望塔',2,6,1,1),('fountain','廣場噴泉',3,3,1,1),('keep','王宮主堡',12,12,1,1),('statue','首都雕像',2,3,1,1),
   ('watermill','水磨坊（磨坊左 4 格、水車右 2 格，水車轉動）',6,4,1,4),('boat_{i}','小船',2,1,2,1),('ferry','渡船',2,2,1,1),
   ('ruin_small','遺跡（小）',3,3,1,1),('ruin_big','遺跡（大）',5,5,1,1)]
for name,zh,w,h,n,fr in C:
    for i in range(1,n+1): add('C',name.format(i=i),zh,w,h,frames=fr,note=('動畫 4 格橫排在同一張' if fr>1 else '')+('；屋頂畫中性灰階（程式染色）' if 'house' in name or name in('hut_{i}','inn','hall_village','hall_town','farmhouse_{i}') else ''))
for p in ['left','mid','right']:
    for i in (1,2): add('C',f'row_{p}_{i}',f'連棟屋（{ {"left":"左端","mid":"中段","right":"右端"}[p]}，可左右接成一排）',3,5,note='屋頂中性灰階')
for p,zh in [('root','岸邊根部'),('mid','中段'),('end','末端')]: add('C',f'pier_{p}',f'碼頭棧道（{zh}，可接長）',2,1)
for d in 'hv':
    for p,zh in [('end','端點'),('mid','中段')]: add('C',f'bridge_{d}_{p}',f'木橋（{"橫" if d=="h" else "豎"}向{zh}）',1,1)
for s,sz in [('s',3),('m',4),('l',6)]:
    for st,zh in [(1,'打樁'),(2,'骨架'),(3,'上樑')]: add('C',f'site_{s}_{st}',f'工地（{zh}）',sz,sz)
# D
for k,zh,hgt,gw in [('fence','圍籬',1,2),('palisade','木柵',2,2),('stone','石牆',3,3),('brick','磚牆',3,3)]:
    add('D',f'{k}_h',f'{zh}：橫段正面（左右可重複）',1,hgt)
    add('D',f'{k}_v',f'{zh}：縱段（俯視牆頂，上下可重複）',1,1)
    for c in ['nw','ne','sw','se']: add('D',f'{k}_corner_{c}',f'{zh}：外角（角樓）{c}',1,hgt)
    for e in 'we': add('D',f'{k}_end_{e}',f'{zh}：橫段{"左" if e=="w" else "右"}端收尾',1,hgt)
    for e in 'ns': add('D',f'{k}_end_{e}',f'{zh}：縱段{"上" if e=="n" else "下"}端收尾',1,1)
    add('D',f'{k}_gate_h',f'{zh}：橫牆城門（中間是路）',gw,hgt)
    add('D',f'{k}_gate_v',f'{zh}：縱牆城門（中間一格是路）',1,3)
    add('D',f'{k}_tee',f'{zh}：橫牆與縱牆的 T 字接頭',1,hgt)
# E
E=[('barrel','木桶',1,1),('crate','木箱',1,1),('haystack','乾草堆',1,1),('woodpile','柴堆',1,1),('cart','推車',2,1),('bench','長椅',2,1),('torch','路燈（火把）',1,2),
   ('signboard','告示牌',1,1),('clothesline','晾衣繩',2,1),('coop','雞舍',1,1),('doghouse','狗屋',1,1),('shed','小棚屋',2,2),('flowerbed','花圃',1,1),
   ('scarecrow','菜園稻草人',1,2),('bucket','井邊水桶',1,1),('awning','市集遮陽棚（布中性灰階）',3,2),('flagpole','旗竿（旗中性灰階）',1,3)]
for n,zh,w,h in E: add('E',f'p_{n}',zh,w,h,note='透明背景')
add('E','w_hedge','籬笆片（Wang 2-corner 16 格一組，4×4 排）',4,4,note='透明背景')
for n,zh,w,h in [('orchard_apple','蘋果樹',2,3),('orchard_pear','梨樹',2,3),('orchard_plum','李樹',2,3),('bush_1','灌木叢',1,1),('bush_2','灌木叢',1,1),('reeds','蘆葦叢（水岸）',1,1),('weeds','牆邊雜草',1,1),('log','倒木',2,1)]:
    add('G',f'n_{n}',zh,w,h,note='透明背景')
# F (pixel sizes)
for p in ['head','body','tail']: add('F',f'sym_arrow_{p}',f'戰爭箭頭（紅，{p}，可拉長，向右）',32,32,unit='px')
for n,zh in [('battle','交戰標記（交叉劍）'),('siege','圍城標記'),('settlers','開拓隊旗符'),('migrants','遷徙隊伍旗符')]: add('F',f'sym_{n}',zh,32,32,unit='px')
for a in 'ab': add('F',f'sym_flag_{a}','勢力旗（灰階，程式染勢力色；飄動）',16,32,unit='px',frames=4,note='動畫 4 格橫排')
for n,zh in [('disaster','天災標記'),('tax','糧稅（穀袋往上）'),('crown','首都皇冠'),('plague','疫病')]: add('F',f'sym_{n}',zh,24,24,unit='px')
order={'A':1,'B':2,'F':3,'C':4,'D':5,'E':6,'G':7}
rows.sort(key=lambda r:order[r[0]])
with open('assets/GPT_第十一批_工作清單.csv','w',newline='',encoding='utf-8') as f:
    w=csv.writer(f); w.writerow(['序號','組','檔名','內容','原尺寸寬','原尺寸高','動畫格數','輸出寬','輸出高','說明'])
    for i,r in enumerate(rows,1): w.writerow([i,r[0],r[1],r[2],r[3],r[4],r[5],r[6],r[7],r[8]])
print(len(rows)); import collections; print(collections.Counter(r[0] for r in rows))
print(max(r[6] for r in rows), max(r[7] for r in rows))
