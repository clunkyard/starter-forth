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


if __name__ == "__main__":
    robot = Starter(str(Path(__file__).with_name("tactic.fs")))
    try:
        cy.run(robot, binding_name="clunkyard-forth")
    finally:
        robot.forth.close()
