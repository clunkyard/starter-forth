\ A starter robot for rules v2, on the installed Clunkyard Forth adapter.
\ It sweeps a medium scan to
\ find a robot, or points it where a passive contact was heard, narrows the
\ scan to hold it, leads it with shots whose power suits the range, fires
\ only when the turret is on target, keeps battery back for driving and
\ keeps out of the storm.
\
\ The trade-offs (the game words have the numbers from hello):
\   scan     a narrow arc reaches further; a wide one finds robots nearby
\   power    heavy shots hit harder for less battery, but fly slowly
\   battery  shots and driving share it, and it refills slowly
\   turret   it turns at a limited rate; aim-heading is where it points now
\   cues     passive contacts (a ping, a scan beam, a shot heard) say which
\            way to look, not where to shoot: they have no id and a noisy
\            bearing, so nearest leaves them out
\ Clunkyard robot API 2.0.0: one core under every language, the same
\ answers everywhere. Facts (a call shows its arguments):
\ match: mode ruleset control robots is_3d tick_rate max_ticks
\   robot_id(slot)
\ clock: tick seconds remaining_ticks
\ arena: size gravity terrain_kind ground_height(x y)
\ me: health position velocity heading pitch roll angular_velocity
\   attitude grounded altitude applied
\ battery: charge capacity regen sustainable_drive compute
\   drive_cost(fraction)
\ drive: hover_lift
\ gun: aim_now cooldown_ticks spread max_power damage_scale
\   can_fire(power reserve) shot_speed(power) shot_cost(power)
\   reload_ticks(power) shot_damage(power) power_for_speed(speed)
\   power_for_flight_time(range seconds) lead(contact power)
\   lead_point(position velocity power)
\ scanner: last proximity_range reach(arc) arc_for_reach(range)
\ sensors: contacts nearest cues contact(slot) position_of(contact)
\ tracker: cue nearest_bearing_rate velocity(slot) position(slot)
\   last_seen(slot)
\ nav: zone zone.margin(point) zone.contains(point margin)
\   zone.distance_from(point) storm storm.margin(point)
\   storm.contains(point margin) storm.distance_from(point) hill
\   hill.margin(point) hill.contains(point margin)
\   hill.distance_from(point) pickups nearest_pickup(kind)
\   field_distance(a b)
\ score: me best armour
\ angles: normalize(degrees) angle_diff(target current)
\   bearing_to(from to) elevation_to(from to) direction(heading pitch)
\   distance(a b) clamp(value low high)
\ raw: number(path) text(path) has(path) flag(path) count(path) line()
\ Controls: drive.set(throttle turn pitch_turn)
\   drive.forces(thrust strafe lift yaw_torque pitch_torque roll_torque brake)
\   gun.aim(heading pitch) gun.fire(power) scanner.set(heading arc pitch)
\   tracker.configure(smoothing forget_ticks cue_ticks)
\   status.set(text value) raw.extra(json)
\ Every control argument is optional; a second call keeps what the first
\ set. Absent facts are your language's none. Names are snake_case here;
\ your language cases them its own way. Angles are degrees from the +x
\ axis; pitch is positive up.
\ Manual: https://clunkyard.com/manual#sdk

0.33e fconstant search-turn  \ enough to turn against the brake in the force rulesets
0 value free?                \ flying free, robots may be above or below

: on-start ( -- ) mode-3d? gravity-z f0= and to free? ;

: steer ( F: target current per -- turn ) frot frot angle-diff fswap f/ clamp ;

: track ( i -- )
  { t }  t contact-range { f: range }
  range 350e f> if 0.7e else range 200e f< if -0.4e else 0.2e then then
  t contact-bearing heading 45e steer
  t contact-elevation pitch f- 45e f/ clamp drive!
  t contact-bearing 20e t contact-elevation scan!  \ narrow: holds it, reaching about 735
  \ The heaviest shot that still covers the range in about a second, aimed
  \ where the target will be, lobbed under gravity.
  range 1e power-for-flight-time { f: power }
  t contact-position t contact-velocity power lead
  if fdrop else t contact-bearing t contact-elevation then { f: h f: p }
  h p aim!
  h aim-heading angle-diff fabs 3e f<  p aim-pitch f- fabs 3e f< and
  power 10e can-fire? and if power fire! then
  s" TRACK" range status! ;

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
  scan-heading 36e f+ fnormalize 40e
  free? if tick 10 / 3 mod 1- 35 * s>f else 0e then scan!
  s" SEARCH" status-text! ;

: on-turn ( -- )
  0.2e brake!
  nearest dup 0>= if track else drop cue? if follow-cue else sweep then then
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
