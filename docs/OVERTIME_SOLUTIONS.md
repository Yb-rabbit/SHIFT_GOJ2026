# OVERTIME Solutions

Generated from `OvertimeCollisionCatalog`. 8 fixed problems.

## 01 · MERGE

- File: `OT-PROBLEM_001.json`
- Grid: 7x6 · Emitters: 2 · Receivers: 1 · Slots: 2 · Expected conflicts: 1 pair(s)

```
    0123456
00  #######
01  E...+.R
02  ####.##
03  ####.##
04  E...+##
05  #######
```

Legend: `E` emitter · `R` receiver · `+` slot · `.` corridor · `#` wall

| Slot | Coord | Piece | Rotation |
| ---: | :---: | :--- | ---: |
| 1 | 4,4 | FILTER 1 | 1 |
| 2 | 4,1 | FILTER 1 | 0 |

Hand order:

- FILTER 1 → (4,4) rot 1
- FILTER 1 → (4,1) rot 0

## 02 · CROSSING

- File: `OT-PROBLEM_002.json`
- Grid: 7x6 · Emitters: 2 · Receivers: 2 · Slots: 1 · Expected conflicts: 0 pair(s)

```
    0123456
00  ####E##
01  ####.##
02  ##E+..R
03  ####.##
04  ####R##
05  #######
```

Legend: `E` emitter · `R` receiver · `+` slot · `.` corridor · `#` wall

| Slot | Coord | Piece | Rotation |
| ---: | :---: | :--- | ---: |
| 1 | 3,2 | DELAY 1 | 0 |

Hand order:

- DELAY 1 → (3,2) rot 0

## 03 · INTERLEAVE

- File: `OT-PROBLEM_003.json`
- Grid: 7x6 · Emitters: 2 · Receivers: 1 · Slots: 2 · Expected conflicts: 2 pair(s)

```
    0123456
00  #######
01  #######
02  E...+.R
03  ####+##
04  ####E##
05  #######
```

Legend: `E` emitter · `R` receiver · `+` slot · `.` corridor · `#` wall

| Slot | Coord | Piece | Rotation |
| ---: | :---: | :--- | ---: |
| 1 | 4,3 | SPLITTER | 2 |
| 2 | 4,2 | NOT | 0 |

Hand order:

- SPLITTER → (4,3) rot 2
- NOT → (4,2) rot 0

## 04 · INVERTED MERGE

- File: `OT-PROBLEM_004.json`
- Grid: 9x8 · Emitters: 2 · Receivers: 1 · Slots: 3 · Expected conflicts: 1 pair(s)

```
    012345678
00  #########
01  #########
02  E...+..+R
03  ####.####
04  ####.####
05  E...+####
06  #########
07  #########
```

Legend: `E` emitter · `R` receiver · `+` slot · `.` corridor · `#` wall

| Slot | Coord | Piece | Rotation |
| ---: | :---: | :--- | ---: |
| 1 | 4,5 | FILTER 1 | 1 |
| 2 | 4,2 | FILTER 1 | 0 |
| 3 | 7,2 | NOT | 0 |

Hand order:

- FILTER 1 → (4,5) rot 1
- FILTER 1 → (4,2) rot 0
- NOT → (7,2) rot 0

## 05 · FILTERED CROSSING

- File: `OT-PROBLEM_005.json`
- Grid: 9x8 · Emitters: 2 · Receivers: 2 · Slots: 4 · Expected conflicts: 0 pair(s)

```
    012345678
00  #####E###
01  #####.###
02  #####.###
03  #####+###
04  E...+..+R
05  #####.###
06  #####+###
07  #####R###
```

Legend: `E` emitter · `R` receiver · `+` slot · `.` corridor · `#` wall

| Slot | Coord | Piece | Rotation |
| ---: | :---: | :--- | ---: |
| 1 | 4,4 | DELAY 1 | 0 |
| 2 | 7,4 | FILTER 1 | 0 |
| 3 | 5,3 | NOT | 3 |
| 4 | 5,6 | FILTER 0 | 3 |

Hand order:

- DELAY 1 → (4,4) rot 0
- FILTER 1 → (7,4) rot 0
- NOT → (5,3) rot 3
- FILTER 0 → (5,6) rot 3

## 06 · SPLIT SIGNAL

- File: `OT-PROBLEM_006.json`
- Grid: 9x8 · Emitters: 2 · Receivers: 2 · Slots: 4 · Expected conflicts: 2 pair(s)

```
    012345678
00  #########
01  #########
02  E...++.R#
03  ####++###
04  ####.R###
05  ####.####
06  ####E####
07  #########
```

Legend: `E` emitter · `R` receiver · `+` slot · `.` corridor · `#` wall

| Slot | Coord | Piece | Rotation |
| ---: | :---: | :--- | ---: |
| 1 | 4,3 | SPLITTER | 2 |
| 2 | 4,2 | NOT | 0 |
| 3 | 5,2 | SPLITTER | 0 |
| 4 | 5,3 | NOT | 3 |

Hand order:

- SPLITTER → (4,3) rot 2
- NOT → (4,2) rot 0
- SPLITTER → (5,2) rot 0
- NOT → (5,3) rot 3

## 07 · DOUBLE PROBE

- File: `OT-PROBLEM_007.json`
- Grid: 11x8 · Emitters: 3 · Receivers: 1 · Slots: 4 · Expected conflicts: 2 pair(s)

```
    01234567890
00  ###########
01  ###########
02  E...+...+.R
03  ####+###+##
04  ####.###.##
05  ####.###.##
06  ####E###E##
07  ###########
```

Legend: `E` emitter · `R` receiver · `+` slot · `.` corridor · `#` wall

| Slot | Coord | Piece | Rotation |
| ---: | :---: | :--- | ---: |
| 1 | 4,3 | SPLITTER | 2 |
| 2 | 4,2 | NOT | 0 |
| 3 | 8,3 | SPLITTER | 2 |
| 4 | 8,2 | NOT | 0 |

Hand order:

- SPLITTER → (4,3) rot 2
- NOT → (4,2) rot 0
- SPLITTER → (8,3) rot 2
- NOT → (8,2) rot 0

## 08 · FINAL CASCADE

- File: `OT-PROBLEM_008.json`
- Grid: 13x9 · Emitters: 2 · Receivers: 2 · Slots: 4 · Expected conflicts: 7 pair(s)

```
    0123456789012
00  #############
01  #############
02  E.....++.R###
03  ######++#####
04  ######.R#####
05  ######.######
06  ######.######
07  ######E######
08  #############
```

Legend: `E` emitter · `R` receiver · `+` slot · `.` corridor · `#` wall

| Slot | Coord | Piece | Rotation |
| ---: | :---: | :--- | ---: |
| 1 | 6,3 | NOT | 1 |
| 2 | 6,2 | DELAY 1 | 0 |
| 3 | 7,2 | SPLITTER | 0 |
| 4 | 7,3 | NOT | 3 |

Hand order:

- NOT → (6,3) rot 1
- DELAY 1 → (6,2) rot 0
- SPLITTER → (7,2) rot 0
- NOT → (7,3) rot 3

