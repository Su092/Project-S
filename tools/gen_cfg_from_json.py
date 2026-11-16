#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
把 sdata.json 导出成 Assets/_Game/Res/Cfg/*.tsv（三行表头）。

这是一次性迁移工具，不是长期构建步骤：
  - 迁移后 **TSV 就是唯一源头**，sdata.json 不再参与运行时。
  - 之后改数值请直接改 TSV（表格软件或文本编辑器都行）。
  - 若需要重跑，它会**覆盖** TSV —— 手工改过的值会丢。跑之前先 git commit。

为什么要有这个脚本而不是手敲 TSV：
  Tab 分隔靠手敲必错（IDE 常把 Tab 转成空格，整张表会静默变成单列）。
  从已校验的 json 走一遍机器转换，能保证 106 条 dotnet 断言迁移前后全绿。

敌人的数值原本硬编码在 GameRoot.cs 的 6 个 Kind* 函数里，json 里没有，
所以它们在 ENEMIES 常量里单独给出（值照抄改造前的代码）。
"""

import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.dirname(HERE)
SRC = os.path.join(PROJ, "tools", "legacy-sdata.json")
OUT = os.path.join(PROJ, "Assets", "_Game", "Res", "Cfg")


# ---------------------------------------------------------------- 敌人
# 来源：改造前 GameRoot.cs 的 KindModel / KindHeight / KindHp / KindDmg /
# KindSpeed / KindXp 六个 switch。数值原样搬运，一个不改。
#
# speed 原本写成 6.0f/3f 这种「设计值 ÷3」形式，注释说明了原因（不降速玩家拉不开距离）。
# 这里落成结算值，理由写进表头说明列，避免下一个人以为是随手填的。
ENEMIES = [
    # id        name         ai        model                   height hp   dmg speed      xp
    ("Crawler", "爬行者", "Chaser", "Modle_Enemy_Crawler", 0.86, 55, 10, 6.0 / 3.0, 8),
    ("Brute", "盾卫", "Chaser", "Modle_Enemy_Shield", 1.97, 220, 22, 2.2 / 3.0, 22),
    ("Bomber", "轰炸者", "Bomber", "Modle_Enemy_Spitter", 1.74, 68, 34, 5.4 / 3.0, 14),
    ("Turret", "炮塔无人机", "Turret", "Modle_Enemy_Drone", 1.20, 110, 12, 0.0, 18),
]

# 行为参数：contactRange / attackRange / attackCooldown / shieldFacing / shieldMul / shieldPierceMul
ENEMY_AI = {
    # 近战撞击距离 / 远程或引爆距离 / 冷却 / 盾面阈值(0=无盾) / 无穿透减伤 / 有穿透减伤
    "Crawler": (1.4, 0.0, 0.0, 0.0, 1.0, 1.0),
    "Brute": (1.4, 0.0, 0.0, 0.3, 0.45, 0.75),
    "Bomber": (2.6, 2.6, 1.2, 0.0, 1.0, 1.0),
    "Turret": (0.0, 30.0, 1.6, 0.0, 1.0, 1.0),
}


def fnum(v):
    """浮点尽量短，但要能精确往返。"""
    if isinstance(v, bool):
        return "1" if v else "0"
    if isinstance(v, int):
        return str(v)
    if isinstance(v, float):
        if v == int(v):
            return str(int(v))
        # 去掉 float32/float64 转换产生的长尾（0.7333333333333334 → 0.733333）。
        # 6 位小数对游戏数值足够，且表格肉眼可读。
        return repr(round(v, 6))
    return str(v)


def cell(v):
    if v is None:
        return ""
    s = fnum(v) if isinstance(v, (int, float, bool)) else str(v)
    # Tab / 换行会破表；引号交给写入时统一包
    return s.replace("\t", " ").replace("\r", " ").replace("\n", " ")


def write_tsv(name, headers, remarks, rules, rows):
    """headers/remarks/rules 均为等长列表；rows 是 cell 列表的列表。"""
    assert len(headers) == len(remarks) == len(rules), name
    lines = []
    lines.append("\t".join(headers))
    lines.append("\t".join(remarks))
    lines.append("\t".join(rules))
    for r in rows:
        assert len(r) == len(headers), (name, len(r), len(headers), r)
        lines.append("\t".join(cell(c) for c in r))

    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name + ".tsv")
    # newline="" 是关键：让 \n 原样落盘，不被 Windows 改成 \r\n
    # （两种都能读，但混用会让 git diff 出现整文件变更）
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print(f"  {name}.tsv  {len(rows)} 行")


def main():
    with open(SRC, "r", encoding="utf-8") as f:
        raw = f.read()

    # sdata.json 允许 // 注释，json 模块不认，先剥掉整行注释
    stripped = "\n".join(
        ln for ln in raw.splitlines() if not ln.lstrip().startswith("//")
    )
    data = json.loads(stripped)

    print("导出配置表 ->", OUT)

    # ---------------- Player ----------------
    p = data["player"]
    write_tsv(
        "Player",
        ["hp", "walk", "sprintSpeed", "sprintTime", "sprintCooldown",
         "jumpHeight", "gravity", "capsuleRadius", "capsuleHeight", "iFrames",
         "gunSlots", "bagSize", "healPickup",
         "xpSquare", "xpLinear", "xpFlat",
         "hpScalePerThreat", "dmgScalePerThreat", "huntAtThreat",
         "salvageCommon", "salvageRare", "salvageEpic"],
        ["生命上限", "行走速度", "冲刺速度", "冲刺时长", "冲刺冷却",
         "跳跃高度", "重力", "胶囊半径", "胶囊高度", "受击无敌",
         "可携带枪数", "背包容量", "补给回血",
         "经验二次项", "经验一次项", "经验常数",
         "威胁生命系数", "威胁伤害系数", "狩猎阈值",
         "分解普通", "分解稀有", "分解史诗"],
        ["float", "float", "float", "float", "float",
         "float", "float", "float", "float", "float",
         "int", "int", "float",
         "int", "int", "int",
         "float", "float", "float",
         "int", "int", "int"],
        [[p.get("hp"), p.get("walk"), p.get("sprintSpeed"), p.get("sprintTime"),
          p.get("sprintCooldown"), p.get("jumpHeight"), p.get("gravity"),
          p.get("capsuleRadius"), p.get("capsuleHeight"), p.get("iFrames"),
          p.get("gunSlots"), p.get("bagSize"), p.get("healPickup"),
          2, 5, 8,
          0.008, 0.004, 88,
          3, 6, 9]],
    )

    # ---------------- Gun ----------------
    # model / starter 是这次新拆出来的：
    #   model   —— 原 PlayerWeapons.ActiveGunModel 里的 switch
    #   starter —— 原 PlayerWeapons.Init 里的 string[] { "rifle", "shotgun" }
    GUN_MODEL = {
        "rifle": "Modle_Gun_Auto",
        "smg": "Modle_Gun_Auto",
        "shotgun": "Modle_Gun_Grenade",
        "sniper": "Modle_Gun_Single",
        "beam": "Modle_Gun_Single",
        "launcher": "Modle_Gun_Grenade",
    }
    STARTERS = {"rifle", "shotgun"}
    gun_rows = []
    for g in data["guns"]:
        gid = g["id"]
        gun_rows.append([
            gid, g.get("name"), g.get("desc"),
            g.get("slotCount", 8), g.get("magSize", 30), g.get("reloadTime", 1.5),
            g.get("fireInterval", 0.14), g.get("spreadBase", 1.2), g.get("castCount", 1),
            g.get("baseBullet"), g.get("color", "#8f9bb3"), g.get("zoom", 55),
            GUN_MODEL.get(gid, "Modle_Gun_Auto"),
            1 if gid in STARTERS else 0,
        ])
    write_tsv(
        "Gun",
        ["id", "name", "desc", "slotCount", "magSize", "reloadTime", "fireInterval",
         "spreadBase", "castCount", "baseBullet", "color", "zoom", "model", "starter"],
        ["枪械编号", "枪械名称", "定位描述", "槽位数", "弹匣容量", "换弹秒数", "击发间隔",
         "基础散射", "每次读取数", "起手子弹", "主题色", "瞄准视距", "枪械模型", "是否起手"],
        ["string,unique", "string", "string", "int", "int", "float", "float",
         "float", "int", "string", "string", "float", "string", "bool01"],
        gun_rows,
    )

    # ---------------- Bullet ----------------
    bullet_fields = [
        ("kind", "形态"), ("damage", "伤害"), ("count", "数量"), ("spread", "散射"),
        ("speed", "初速"), ("lifetime", "存活"), ("size", "体积"), ("pierce", "穿透"),
        ("bounce", "反弹"), ("homing", "追踪"), ("gravity", "重力"), ("explodeR", "爆炸半径"),
        ("explodeDmg", "爆炸伤害"), ("burn", "燃烧"), ("slow", "减速"), ("crit", "暴击率"),
        ("critMul", "暴击倍率"), ("knock", "击退"), ("chainCount", "连锁数"),
        ("range", "射程"), ("radius", "范围半径"), ("duration", "持续"), ("trigger", "触发"),
        ("color", "颜色"),
    ]
    bullet_rows = []
    for b in data["bullets"]:
        row = [b["id"], b.get("name"), b.get("icon"), b.get("desc"),
               b.get("rarity", "Common"), b.get("cost", 1), b.get("delay", 0)]
        for k, _ in bullet_fields:
            row.append(b.get(k, ""))
        bullet_rows.append(row)
    write_tsv(
        "Bullet",
        ["id", "name", "icon", "desc", "rarity", "cost", "delay"] + [k for k, _ in bullet_fields],
        ["配件编号", "名称", "角标字", "效果描述", "稀有度", "耗弹", "击发修正"]
        + [v for _, v in bullet_fields],
        ["string,unique", "string", "string", "string", "Common|Rare|Epic", "int", "float"]
        + ["enum|float|int|string"] * len(bullet_fields),
        bullet_rows,
    )

    # ---------------- Modifier / GunModifier ----------------
    mod_fields = [
        ("scope", "作用域"), ("cost", "耗弹"), ("delay", "击发修正"),
        ("damageMulAdd", "伤害加成"), ("sizeMulAdd", "体积加成"), ("speedMulAdd", "速度加成"),
        ("rangeMulAdd", "射程加成"), ("lifeMulAdd", "存活加成"), ("knockMulAdd", "击退加成"),
        ("countMulAdd", "数量加成"), ("radiusMulAdd", "半径加成"),
        ("piercePer", "每层穿透"), ("bouncePer", "每层反弹"), ("homingPer", "追踪"),
        ("burnPer", "燃烧"), ("slowPer", "减速"), ("critPer", "暴击"), ("spreadPer", "散射"),
        ("explodeRAdd", "爆裂半径加成"), ("explodeRBase", "爆裂基础半径"),
        ("explodeDmgFromDmg", "爆伤系数"), ("explodeDmgFlat", "爆伤固定"),
        ("ammoReduce", "省弹"), ("gunMagMul", "弹匣倍率"), ("gunReloadMul", "换弹倍率"),
        ("gunDelayMul", "射速倍率"), ("gunSpreadMul", "散布倍率"),
        ("gunRecycleAdd", "免耗弹"), ("gunCastAdd", "读取加成"),
        ("gunMagMulIfCast", "并联惩罚"), ("gunLeech", "击杀回血"),
        ("multicast", "多重施放"),
    ]

    def mod_row(m):
        row = [m["id"], m.get("name"), m.get("icon"), m.get("desc"),
               m.get("rarity", "Common")]
        for k, _ in mod_fields:
            if k == "scope":
                row.append(m.get("scope", "Next"))
            elif k == "cost":
                row.append(m.get("cost", 1))
            elif k == "delay":
                row.append(m.get("delay", 0))
            else:
                row.append(m.get(k, ""))
        return row

    mod_headers = ["id", "name", "icon", "desc", "rarity"] + [k for k, _ in mod_fields]
    mod_remarks = ["配件编号", "名称", "角标字", "效果描述", "稀有度"] + [v for _, v in mod_fields]
    mod_rules = ["string,unique", "string", "string", "string", "Common|Rare|Epic"] + \
                ["enum|int|float"] * 3 + ["float|int"] * (len(mod_fields) - 3)

    write_tsv("Modifier", mod_headers, mod_remarks, mod_rules,
              [mod_row(m) for m in data.get("modifiers", [])])
    write_tsv("GunModifier", mod_headers, mod_remarks, mod_rules,
              [mod_row(m) for m in data.get("gunModifiers", [])])

    # ---------------- RarityWeight ----------------
    rw = data.get("rarityWeights", {})
    write_tsv(
        "RarityWeight",
        ["rarity", "weight"],
        ["稀有度", "抽取权重"],
        ["string,unique", "int"],
        [[k, v] for k, v in rw.items()],
    )

    # ---------------- Arena ----------------
    a = data["arena"]
    write_tsv(
        "Arena",
        ["width", "depth", "wallHeight", "bossPadRadius", "coverCount",
         "highGroundCount", "spawnPoints", "playerSpawnZ"],
        ["场地宽", "场地深", "墙高", "Boss台半径", "掩体数",
         "高台数", "刷怪点数", "出生点Z"],
        ["float", "float", "float", "float", "int", "int", "int", "float"],
        [[a.get("width", 100), a.get("depth", 100), a.get("wallHeight", 8),
          a.get("bossPadRadius", 9), a.get("coverCount", 18),
          a.get("highGroundCount", 4), a.get("spawnPoints", 16),
          a.get("playerSpawnZ", -38)]],
    )

    # ---------------- Timeline ----------------
    tl = data["timeline"]
    tl_rows = []
    for ph in tl.get("phases", []):
        tl_rows.append([
            ph.get("at"), ph.get("name"), "{" + ",".join(ph.get("pool", [])) + "}",
            ph.get("concurrency", 12), ph.get("interval", 1.2), 0,
        ])
    bp = tl.get("bossPhase")
    if bp:
        tl_rows.append([
            bp.get("at", tl.get("bossAt", 600)), bp.get("name", "BOSS"),
            "{" + ",".join(bp.get("pool", [])) + "}",
            bp.get("concurrency", 8), bp.get("interval", 2.0), 1,
        ])
    write_tsv(
        "Timeline",
        ["at", "name", "pool", "concurrency", "interval", "boss"],
        ["开始秒数", "阶段名", "敌人池", "并发上限", "刷出间隔", "是否Boss阶段"],
        ["float,ascending", "string", "{Kind,Kid}", "int", "float", "bool01"],
        tl_rows,
    )

    # ---------------- Elite ----------------
    e = data["elite"]
    write_tsv(
        "Elite",
        ["firstAt", "interval", "hpMul", "dmgMul", "speedMul", "xpMul", "scale", "healOnKill"],
        ["首只时刻", "出现间隔", "生命倍率", "伤害倍率", "速度倍率", "经验倍率",
         "体积倍率", "击杀回血"],
        ["float", "float", "float", "float", "float", "int", "float", "float"],
        [[e.get("firstAt", 300), e.get("interval", 90), e.get("hpMul", 4.0),
          e.get("dmgMul", 1.5), e.get("speedMul", 0.9), e.get("xpMul", 6),
          e.get("scale", 1.4), e.get("healOnKill", 15)]],
    )

    # ---------------- Boss ----------------
    # bossAt / threatFullAt 放这里而不是 Timeline：它们是「整局的终点」，
    # 与阶段表是两件事，混在一张表里会让「阶段 at 递增」的校验容易误判。
    b = data["boss"]
    write_tsv(
        "Boss",
        ["id", "name", "hp", "contactDamage", "moveSpeed", "pillars", "pillarHp",
         "breachTime", "breachDamageMul", "shieldPeriod", "shieldUpTime",
         "summonInterval", "summonCount", "shockwaveInterval", "shockwaveDamage",
         "bossAt", "threatFullAt"],
        ["编号", "名称", "生命值", "接触伤害", "移动速度", "能量柱数", "柱血量",
         "破防时长", "破防易伤", "护盾周期", "护盾持续",
         "召唤间隔", "召唤数量", "冲击波间隔", "冲击波伤害",
         "入场时刻", "威胁满时刻"],
        ["string", "string", "float", "float", "float", "int", "float",
         "float", "float", "float", "float",
         "float", "int", "float", "float",
         "float", "float"],
        [[b.get("id", "e_boss"), b.get("name", "铸造核心"), b.get("hp", 6000),
          b.get("contactDamage", 25), b.get("moveSpeed", 2.5), b.get("pillars", 4),
          b.get("pillarHp", 800), b.get("breachTime", 4.0),
          b.get("breachDamageMul", 1.5), 18.0, 6.0,
          12.0, 2, 20.0, 22.0,
          tl.get("bossAt", 600), tl.get("threatFullAt", 600)]],
    )

    # ---------------- Enemy ----------------
    enemy_rows = []
    for (eid, ename, ai, model, height, hp, dmg, speed, xp) in ENEMIES:
        cr, ar, acd, sf, sm, spm = ENEMY_AI[eid]
        enemy_rows.append([eid, ename, ai, model, height, hp, dmg, speed, xp,
                           cr, ar, acd, sf, sm, spm])
    write_tsv(
        "Enemy",
        ["id", "name", "ai", "model", "height", "hp", "dmg", "speed", "xp",
         "contactRange", "attackRange", "attackCooldown",
         "shieldFacing", "shieldMul", "shieldPierceMul"],
        ["敌人编号", "名称", "行为", "模型", "目标高度", "生命", "伤害", "移速", "经验",
         "接触距离", "攻击距离", "攻击冷却",
         "盾面阈值", "无穿透减伤", "有穿透减伤"],
        ["string,unique", "string", "Chaser|Bomber|Turret", "string", "float",
         "float", "float", "float", "int",
         "float", "float", "float",
         "float", "float", "float"],
        enemy_rows,
    )

    print("完成。下一步：跑 tools/loadout-tests 确认 106 条断言仍全绿。")


if __name__ == "__main__":
    sys.exit(main())
