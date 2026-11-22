# Edit tactic.fs to change how the robot plays.
from pathlib import Path

from clunkyard_forth import run


if __name__ == "__main__":
    run(str(Path(__file__).with_name("tactic.fs")))
