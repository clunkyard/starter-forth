# A starter robot for rules v2, in Forth: tactic.fs decides each tick with
# the words in clunkyard.fs, and clunkyard_forth.py runs it in Gforth on the
# Clunkyard Python binding (the clunkyard package, which the arena provides),
# which speaks the protocol. The adapter and words are installed; read
# tactic.fs first.
import math
from pathlib import Path

import clunkyard as cy
import clunkyard_forth


class Starter(clunkyard_forth.Tactic):
    def on_turn(self):
        super().on_turn()
        calibrate(self)


# ---- The arena's rate calibration: safe to delete, with the line above ----
# Only the calibration sends calibration.work; it times this language on the
# same work as every other. The kernel is xorshift-sqrt v1: work rounds of a
# 64-bit xorshift and a square-root sum, reported as WORK.
def calibrate(robot):
    work = robot.raw.number("state.calibration.work")
    if not work or work < 1:
        return
    mask, x, total = 0xFFFFFFFFFFFFFFFF, 88172645463325252 ^ robot.clock.tick, 0.0
    for _ in range(int(work)):
        x ^= (x << 13) & mask
        x ^= x >> 7
        x ^= (x << 17) & mask
        total += math.sqrt(x >> 11)
    robot.status.set("WORK", (math.floor(total) + (x >> 40)) % 1000000)


if __name__ == "__main__":
    robot = Starter(str(Path(__file__).with_name("tactic.fs")))
    try:
        cy.run(robot, binding_name="clunkyard-forth")
    finally:
        robot.forth.close()
