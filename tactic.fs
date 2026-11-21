\ A starter robot for rules v2, on the installed Clunkyard Forth adapter.
\ It sweeps a medium scan to
\ find a robot, or points it where a passive contact was heard, narrows the
\ scan to hold it, leads it with shots whose power suits the range, fires
\ only when the turret is on target, keeps battery back for driving and
\ keeps out of the storm.
\
\ Bearing is earned: a scan contact's bearing is off by up to its
\ bearing error (degrees, a fixed share of the arc), so a wide beam finds a
\ robot and a narrow one aims at it. The robot narrows the beam, holds it
\ on the target and aims along a line fitted through its recent sightings.
\ Contacts within 120 units are exact.
\
\ The trade-offs (the game words have the numbers from hello):
\   scan     a narrow arc reaches further; a wide one finds robots nearby
\   power    heavy shots hit harder for less battery, but fly slowly
\   battery  shots and driving share it, and it refills slowly
\   turret   it turns at a limited rate; aim-heading is where it points now
\   cues     passive contacts (a ping, a scan beam, a shot heard) say which
\            way to look, not where to shoot: they have no id and a noisy
\            bearing, so nearest leaves them out
\ Clunkyard robot API 2.1.0: one core under every language, the same
\ answers everywhere. Facts (a call shows its arguments):
\ match: mode ruleset control robots is_3d tick_rate max_ticks
\   robot_id(slot)
\ clock: tick seconds remaining_ticks
\ arena: size gravity terrain_kind ground_height(x y)
\ me: health position velocity heading pitch roll angular_velocity
\   attitude grounded altitude wall_contact hurt applied hull_mounts
\   to_mount(heading pitch) from_mount(heading pitch) mount_toward(point)
\ battery: charge capacity regen sustainable_drive compute
\   drive_cost(fraction)
\ drive: hover_lift
\ gun: aim_now cooldown_ticks spread max_power damage_scale
\   can_fire(power reserve) shot_speed(power) shot_cost(power)
\   reload_ticks(power) shot_damage(power) power_for_speed(speed)
\   power_for_flight_time(range seconds) lead(contact power)
\   lead_point(position velocity power)
\ scanner: last proximity_range reach(arc) arc_for_reach(range) is_on
\ sensors: contacts nearest cues contact(slot) position_of(contact)
\ tracker: cue nearest_bearing_rate velocity(slot) position(slot)
\   history(slot) last_seen(slot)
\ nav: zone zone.margin(point) zone.contains(point margin)
\   zone.distance_from(point) storm storm.margin(point)
\   storm.contains(point margin) storm.distance_from(point) hill
\   hill.margin(point) hill.contains(point margin)
\   hill.distance_from(point) wall_margin() pickups nearest_pickup(kind)
\   field_distance(a b)
\ score: me best armour
\ drone: has ready in_flight launches_left report sightings
\ angles: normalize(degrees) angle_diff(target current)
\   bearing_to(from to) elevation_to(from to) direction(heading pitch)
\   distance(a b) to_chassis(heading pitch attitude)
\   from_chassis(heading pitch attitude) clamp(value low high)
\ raw: number(path) text(path) has(path) flag(path) count(path) line()
\ Controls: drive.set(throttle turn pitch_turn)
\   drive.forces(thrust strafe lift yaw_torque pitch_torque roll_torque brake)
\   gun.aim(heading pitch) gun.fire(power) scanner.set(heading arc pitch)
\   scanner.power(on) tracker.configure(smoothing forget_ticks cue_ticks)
\   drone.launch(heading pitch param) drone.brief(index value)
\   drone.program(language source) status.set(text value) debug.log(line)
\   raw.extra(json)
\ Every control argument is optional; a second call keeps what the first
\ set. Absent facts are your language's none. Names are snake_case here;
\ your language cases them its own way. Angles are degrees from the +x
\ axis; pitch is positive up.
\ Manual: https://clunkyard.com/manual#sdk

0.33e fconstant search-turn  \ enough to turn against the brake in the force rulesets
40e fconstant search-arc     \ degrees: sweeps the circle in half a second
60e fconstant hold-width     \ units across at the target: it stays in the beam from tick to tick
6e fconstant min-arc         \ degrees: the narrowest the beam holds
12 constant fit-window           \ ticks of sightings the aim averages
8 constant fit-sightings         \ narrow sightings in the window before a shot
40 constant look-every       \ ticks between looks around the target, for a nearer robot
10 constant reacquire-ticks  \ ticks to look again where a lost target was
0 value free?                \ flying free, robots may be above or below
0 value target-slot          \ the last target's slot, for looking again where it was lost

: on-start ( -- ) mode-3d? gravity-z f0= and to free? ;

: steer ( F: target current per -- turn ) frot frot angle-diff fswap f/ clamp ;

\ The target's recent sightings, as the tracker keeps them (up to 32).
32 constant history-max
create sight-tick history-max cells allot
create sight-err history-max floats allot
create sight-x history-max floats allot
create sight-y history-max floats allot
create sight-z history-max floats allot
: sight@ ( addr i -- F: v ) floats + f@ ;
: sight! ( addr i F: v -- ) floats + f! ;
: take-sighting ( tick source i F: err range-err x y z -- )
  { tick source i }
  sight-z i sight!  sight-y i sight!  sight-x i sight!  fdrop  sight-err i sight!
  tick sight-tick i cells + ! ;
: load-sightings ( slot -- n )  cy-tracker-history cy-result drop  dup >r 0 ?do i take-sighting loop  r> ;

0 value sight-n  0 value fit-n  fvariable fit-best  fvariable fit-limit  fvariable fit-t0  fvariable fit-spread
fvariable fit-mean-x  fvariable fit-mean-y  fvariable fit-mean-z
fvariable fit-x  fvariable fit-y  fvariable fit-z  fvariable fit-vx  fvariable fit-vy  fvariable fit-vz
: sight-recent? ( i -- f ) cells sight-tick + @ tick fit-window - > ;
: sight-narrow? ( i -- f ) dup sight-recent? swap sight-err swap sight@ fit-limit f@ f<= and ;  \ the narrow ones
: sight-tick-of ( i -- F: tick ) cells sight-tick + @ s>f ;
: sight-x-of ( i -- F: x ) sight-x swap sight@ ;
: sight-y-of ( i -- F: y ) sight-y swap sight@ ;
: sight-z-of ( i -- F: z ) sight-z swap sight@ ;
: sight-spread-of ( i -- F: dt^2 ) sight-tick-of fit-t0 f@ f- fdup f* ;
: sight-cross-x ( i -- F: dt*dx ) dup sight-tick-of fit-t0 f@ f- sight-x-of fit-mean-x f@ f- f* ;
: sight-cross-y ( i -- F: dt*dy ) dup sight-tick-of fit-t0 f@ f- sight-y-of fit-mean-y f@ f- f* ;
: sight-cross-z ( i -- F: dt*dz ) dup sight-tick-of fit-t0 f@ f- sight-z-of fit-mean-z f@ f- f* ;
: sight-total ( xt -- F: sum ) 0e sight-n 0 ?do i sight-narrow? if i over execute f+ then loop drop ;
: sight-mean ( xt -- F: mean ) sight-total fit-n s>f f/ ;
: sight-slope ( xt -- F: per-tick ) sight-total fit-spread f@ f/ ;

\ A straight line through the narrowest of the last window ticks' sightings:
\ where the robot is now (fit-x fit-y fit-z) and how fast it moves
\ (fit-vx fit-vy fit-vz), and how many sightings it rests on, 0 for no fit.
\ Each sighting is off by its bearing error; the line averages that out.
: fit ( slot -- count )
  load-sightings to sight-n
  1e9 fit-best f!
  sight-n 0 ?do i sight-recent? if sight-err i sight@ fit-best f@ fmin fit-best f! then loop
  1.5e fit-best f@ f* fit-limit f!
  0 to fit-n  sight-n 0 ?do i sight-narrow? if fit-n 1+ to fit-n then loop
  fit-n 2 < if 0 exit then
  ['] sight-tick-of sight-mean fit-t0 f!
  ['] sight-x-of sight-mean fit-mean-x f!  ['] sight-y-of sight-mean fit-mean-y f!  ['] sight-z-of sight-mean fit-mean-z f!
  ['] sight-spread-of sight-total fit-spread f!
  tick s>f fit-t0 f@ f- { f: ahead }  tick-rate s>f { f: rate }
  ['] sight-cross-x sight-slope { f: sx }  ['] sight-cross-y sight-slope { f: sy }  ['] sight-cross-z sight-slope { f: sz }
  fit-mean-x f@ sx ahead f* f+ fit-x f!  fit-mean-y f@ sy ahead f* f+ fit-y f!  fit-mean-z f@ sz ahead f* f+ fit-z f!
  sx rate f* fit-vx f!  sy rate f* fit-vy f!  sz rate f* fit-vz f!
  fit-n ;

\ The slot of the nearest contact, for its history.
: nearest-slot ( -- slot ) cy-sensors-nearest cy-result if drop 8 0 do fdrop loop else 0 then ;

\ The beam that is hold-width across at a range: narrow enough for a precise
\ bearing, wide enough to keep the target in it from tick to tick.
: hold-arc ( F: range -- arc ) hold-width 2e f/ fswap fatan2 2e f* 180e f* 3.141592653589793e f/ min-arc fmax ;

: track ( i -- )
  { t }  t contact-range { f: range }
  range 350e f> if 0.7e else range 200e f< if -0.4e else 0.2e then then
  t contact-bearing heading 45e steer
  t contact-elevation pitch f- 45e f/ clamp drive!
  \ Halve the beam each tick until it is hold-width across at the target.
  \ In a crowd a narrow beam sees one robot: every look-every ticks, widen it
  \ for a tick, and a nearer robot it finds becomes the target.
  range hold-arc { f: hold }
  tick look-every mod 0= if search-arc else scan-arc 2e f/ search-arc fmin hold fmax then { f: arc }
  t contact-bearing arc t contact-elevation scan!
  \ The heaviest shot that still covers the range in about a second, aimed
  \ where the target will be, lobbed under gravity.
  range 1e power-for-flight-time { f: power }
  \ An exact contact: lead is the intercept if the target holds course. A
  \ noisy one: lead the fitted line, and fire only once it rests on enough sightings.
  t contact-bearing-error f0> if
    nearest-slot dup to target-slot fit { count }
    count fit-sightings >=
    count 0<> if fit-x f@ fit-y f@ fit-z f@ fit-vx f@ fit-vy f@ fit-vz f@ power lead else false then
  else
    true
    t contact-position t contact-velocity power lead
  then
  if fdrop else t contact-bearing t contact-elevation then { f: h f: p } { ready }
  h p aim!
  h aim-heading angle-diff fabs 3e f<  p aim-pitch f- fabs 3e f< and
  power 10e can-fire? and ready and if power fire! then
  ready if s" TRACK" else s" NARROW" then range status! ;

\ Lost it: look again where it was last seen, widening the beam, before searching.
: lost? ( -- f )
  target-slot 0= if false exit then
  target-slot cy-tracker-last-seen cy-result 0= if false exit then
  tick swap - reacquire-ticks <= ;
: reacquire ( -- )
  target-slot cy-tracker-position cy-result 0= if position then { f: wx f: wy f: wz }
  position wx wy wz bearing-to { f: bearing }
  free? if position wx wy wz elevation-to else 0e then { f: elevation }
  0.5e bearing heading 45e steer elevation pitch f- 45e f/ clamp drive!
  bearing  scan-arc 2e f* search-arc fmin  elevation scan!
  s" REACQUIRE" status-text! ;

\ Head for where it was heard, sweeping an arc that still reaches it across
\ the bearing's error (and, flying free, the elevation's).
: follow-cue ( -- )
  position cue-position bearing-to { f: bearing }
  position cue-position distance { f: dist }
  free? if position cue-position elevation-to else 0e then { f: elevation }
  cue-bearing-error 30e fmin { f: error }
  tick 5 mod 2 - s>f 2e f/ error f* { f: across }
  free? if tick 5 / 3 mod 1- s>f error f* 2e f/ else 0e then { f: up }
  0.7e bearing heading 45e steer elevation pitch f- 45e f/ clamp drive!
  bearing across f+  dist 100e f+ arc-for-reach  elevation up f+ scan!
  s" CUE" dist status! ;

\ Sweeps the circle in half a second, flying free at three heights in turn.
: sweep ( -- )  \ SEARCH is the standard string word
  0.5e search-turn pitch fnegate 45e f/ clamp drive!
  scan-heading 36e f+ fnormalize search-arc
  free? if tick 10 / 3 mod 1- 35 * s>f else 0e then scan!
  s" SEARCH" status-text! ;

\ A simple dodge: vary it, or predict the shooter. A shot heard once the
\ last dodge is over starts the next for half a second.
0 value dodges  0 value dodge-end
: shot-heard? ( -- f ) false cues 0 ?do i cue# contact-source muzzle = or loop ;
\ Off the line of fire: left, then right next time (turn is positive left,
\ strafe positive right).
: left? ( -- f ) dodges 1 and 0<> ;
: dodge ( -- )
  tick dodge-end >= shot-heard? and if  dodges 1+ to dodges  tick 10 + to dodge-end  then
  tick dodge-end < if
    forces? if 0.5e throttle!  left? if -1e else 1e then strafe!
    else 1e throttle!  left? if 1e else -1e then turn! then
    left? if s" dodge:left" else s" dodge:right" then dodges s>f status!
  then ;

: on-turn ( -- )
  0.2e brake!
  nearest dup 0>= if track else drop lost? if reacquire else cue? if follow-cue else sweep then then then
  dodge
  \ Low battery: drive gently so the shots can still be paid for.
  battery battery-capacity 4e f/ f< if throttle@ 0.5e f* throttle! then
  \ The storm hurts inside: drive straight away from its centre.
  storm? if position 60e storm-contains? if
    0.8e throttle!  storm-center position bearing-to heading 30e steer turn!
  then then
  \ Sudden death: head back inside the safe zone.
  zone? if position zone-margin 60e f< if
    0.8e  position zone-center bearing-to heading 30e steer
    zone-shape sphere = if position zone-center elevation-to else 0e then pitch f- 30e f/ clamp drive!
  then then ;
