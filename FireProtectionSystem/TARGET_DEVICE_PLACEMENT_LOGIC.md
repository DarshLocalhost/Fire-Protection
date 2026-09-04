# Target Logic — Where Devices Go

Sprinklers · Smoke Detectors · Notification Appliances

All numbers (spacings, distances, heights) come from a rules file that the engineer fills in.
None of them are in the code. Nothing below invents a number.

---

## 1. The rules file

One file, filled in by the engineer. For each device and each room type it holds:

- how far apart devices may be, at most
- how much floor area one device may cover
- how close, and how far, from a wall
- how far below the ceiling
- what clearance is needed from beams, ducts and columns
- what changes on sloped, high or beamed ceilings

Every value records which standard and table it came from, and whether it is approved.
If any value used in a run is not approved, the result is marked "needs review".

---

## 2. How locations are worked out

Same method for sprinklers, smoke detectors and strobes. Do not search for spots — divide and centre.

```
1. Take the room outline, including any holes in it (shafts, atria, courtyards).

2. Turn the room so the grid follows its own walls, not compass north.

3. Split the room into simple rectangles. Hole areas are left out.

4. For each rectangle, in each direction:
      how many  =  room length divided by the max allowed spacing, rounded UP
      spacing   =  room length divided by that number
      first device sits half a spacing in from the wall
   This makes the spacing and the wall distance correct automatically,
   instead of hoping they come out right.

5. Add more devices if one device would end up covering too much area,
   or if the first one would still sit too far from the wall.

6. Height  =  that ceiling's height, minus the required drop below the ceiling.

7. Only where a device clashes with a beam, duct or column:
      move it slightly, within the allowed window
      if it cannot move, add one instead
      if neither works, flag it for a person

8. Keep the required distance from devices already in the model.

9. Measure the finished layout and record the result:
      is any floor area left uncovered?
      is the biggest gap between devices within the limit?
      is every wall distance within its minimum and maximum?
         not OK  ->  report it, with the measured shortfall
         OK      ->  record which rule justifies each device

10. Rooms too odd-shaped for this fall back to a point-by-point search
    and are flagged for a person to check.
```

---

## 3. Sprinklers

What decides the layout: the room's risk category and the sprinkler type.

On top of the shared method:

- both a minimum **and** a maximum distance to a wall
- a minimum distance between two sprinklers, separate from the maximum spacing
- the drop below the ceiling matters, and is checked again after placing
- beams, ducts and columns each get their own clearance rule, not one shared number
- which way the sprinkler points (down, up, sidewall) has its own limits

---

## 4. Smoke detectors

What decides the layout: the ceiling — its shape, slope and height. Not the room's risk category.

On top of the shared method:

- the allowed spacing is reduced on beamed, sloped or high ceilings
- keep clear of air supply grilles
- mounted on the ceiling, or on a wall within the allowed band near the ceiling

---

## 5. Notification appliances

### 5a. Visible (strobes) — same method as section 2

What decides the layout: the room size against the strobe's light output rating.

On top of the shared method:

- each rating covers a square of a set size; the room is tiled with those squares
- mounting height must sit inside an allowed band
- the device must be visible, not blocked
- corridors are handled as a separate case
- strobes visible from the same spot must flash together

### 5b. Audible (horns, speakers) — a different method

This is not a spacing problem, it is a loudness problem.

```
1. Take the room, its ceiling height, its surfaces, and the assumed background noise level.

2. Pick trial positions and output ratings.

3. For points spread across the room, work out how loud the device is at each one
   (quieter with distance, absorbed by surfaces, reduced through doors and walls).

4. Requirement: loud enough above the background noise, everywhere in the room.
      not met  ->  raise the output, add a device, or move it

5. Report the calculated loudness per room.
```

It shares the model reading and the Revit placement, but not the location maths.

---

## 6. What is different between them

| | Sprinklers | Smoke detectors | Strobes | Horns / speakers |
|---|---|---|---|---|
| Driven by | Room risk category + sprinkler type | Ceiling shape, slope, height | Room size + light output rating | Background noise + surfaces |
| Location maths | Divide and centre | Divide and centre | Divide and centre | Loudness calculation |
| Wall distance | Minimum and maximum | Maximum | Maximum | Not applicable |
| Height | Ceiling minus drop | On or near the ceiling | Mounting band | Mounting band |
| Keep clear of | Beams, ducts, columns | Air supply grilles | Anything blocking view | Doors and walls reduce it |
| Uses the shared method | Yes | Yes | Yes | No |

Three of the four use the same method with a different rules table. Only horns and speakers need
separate maths.

---

## 7. What the engineer must supply

Per device family:

- which standard and edition, plus any local amendments
- the list of room categories to use
- max spacing and area per device, for each room category, device type and ceiling condition
- minimum and maximum distance to a wall
- minimum distance between devices
- clearances from beams, ducts, columns and air grilles
- drop below the ceiling, or the mounting height band
- what changes on sloped, high, beamed or concealed ceilings
- strobes: light output rating against room size
- horns and speakers: assumed background noise, and the required margin above it

Until these are supplied, every result stays marked "needs review".

