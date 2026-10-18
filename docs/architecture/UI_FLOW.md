# Battle Participation UI Flow

## Screen 1 — Side

Battle started.

[ Join Attackers ]
[ Join Defenders ]
[ Spectate ]

## Screen 2 — Character

Show available slots grouped by priority.

### Lords / Heroes
- Lord A
- Hero B

### Officers / Special
- Captain C

### Troop classes
- Archers 1/2
- Infantry 3/7
- Cavalry 0/2

### Spectate
- Watch battle

## Selection feedback

On claim:
- show "Reserving..."
- await authoritative response
- on success: enter control
- on rejection: refresh available slots and let player choose again

## Important

Do not assume the client view is authoritative.
