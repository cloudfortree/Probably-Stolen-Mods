"""Run the managed verification program on the installed game's Mono, without opening a save."""
from ctypes import CDLL, CFUNCTYPE, POINTER, c_char_p, c_float, c_int, c_uint32, c_uint64, c_void_p, cast, string_at
from pathlib import Path
import argparse
import os
import random
import sys


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("game", type=Path)
parser.add_argument("--managed", type=Path, help="Run against a locally archived managed assembly set")
parser.add_argument("--package", type=Path, help="Scan a mod folder instead of the build output")
options = parser.parse_args()
game = options.game
installed_managed = game / "Probably Stolen_Data" / "Managed"
managed = options.managed or installed_managed
mono_root = game / "MonoBleedingEdge"
embed_runtime = mono_root / "EmbedRuntime"
library_path = os.add_dll_directory(str(embed_runtime))
mono = CDLL(str(embed_runtime / "mono-2.0-bdwgc.dll"))


def bind(name, result, *arguments):
    method = getattr(mono, name)
    method.restype = result
    method.argtypes = arguments
    return method


def utf8(path):
    return str(path).encode("utf-8")


bind("mono_set_dirs", None, c_char_p, c_char_p)(utf8(installed_managed), utf8(mono_root / "etc"))
paths = str(managed) if managed == installed_managed else os.pathsep.join((str(managed), str(installed_managed)))
bind("mono_set_assemblies_path", None, c_char_p)(utf8(paths))
bind("mono_config_parse", None, c_char_p)(None)
domain = bind("mono_jit_init_version", c_void_p, c_char_p, c_char_p)(b"stock-verification", b"v4.0.30319")
if not domain:
    raise RuntimeError("Failed to initialize the installed Mono")

add_internal_call = bind("mono_add_internal_call", None, c_char_p, c_void_p)
mono_string_to_utf8 = bind("mono_string_to_utf8", c_void_p, c_void_p)
mono_free = bind("mono_free", None, c_void_p)
samples = random.Random()


@CFUNCTYPE(None, c_int)
def seed(value):
    samples.seed(value)


@CFUNCTYPE(c_int, c_int, c_int)
def range_int(minimum, maximum):
    return samples.randrange(minimum, maximum) if maximum > minimum else minimum


@CFUNCTYPE(c_float, c_float, c_float)
def range_float(minimum, maximum):
    return samples.uniform(minimum, maximum)


@CFUNCTYPE(c_float)
def random_value():
    return samples.random()


@CFUNCTYPE(None, c_int, c_int, c_void_p, c_void_p)
def log(level, options, message, context):
    native = mono_string_to_utf8(message)
    try:
        print(string_at(native).decode("utf-8", errors="replace"), flush=True)
    finally:
        mono_free(native)


# These APIs can be resolved while Harmony compiles the game's UI methods.
game_object = CFUNCTYPE(c_void_p, c_void_p)(lambda _component: None)
set_active = CFUNCTYPE(None, c_void_p, c_int)(lambda _object, _active: None)
play_sound = CFUNCTYPE(None, c_void_p, c_uint64)(lambda _audio, _delay: None)
destroy_object = CFUNCTYPE(None, c_void_p, c_float)(lambda _object, _delay: None)


@CFUNCTYPE(c_int, c_void_p, POINTER(c_uint32))
def parse_color(_text, result):
    # TypeHelper initializes its display palette before the barter factory can roll stock.
    result[0] = 0xFFFFFFFF
    return 1


for name, callback in (
    ("UnityEngine.Random::InitState", seed),
    ("UnityEngine.Random::RandomRangeInt", range_int),
    ("UnityEngine.Random::Range", range_float),
    ("UnityEngine.Random::get_value", random_value),
    ("UnityEngine.DebugLogHandler::Internal_Log", log),
    ("UnityEngine.Component::get_gameObject", game_object),
    ("UnityEngine.GameObject::SetActive", set_active),
    ("UnityEngine.AudioSource::PlayHelper", play_sound),
    ("UnityEngine.Object::Destroy", destroy_object),
    ("UnityEngine.ColorUtility::DoTryParseHtmlColor", parse_color),
):
    add_internal_call(utf8(name), cast(callback, c_void_p))

program = Path(__file__).resolve().parent / "bin/Release/net472/BetterCustomerStock.Verify.exe"
assembly = bind("mono_domain_assembly_open", c_void_p, c_void_p, c_char_p)(domain, utf8(program))
if not assembly:
    raise RuntimeError("Build verification/Verify.csproj in Release mode first")

pager = game / "Mods/Workshop/3835147893/MerchantStockPager.dll"
package = options.package or Path(__file__).resolve().parents[1] / "bin/Release/net472"
arguments = [utf8(program), utf8(managed), utf8(pager), b"--install-patches", utf8(package)]
argv = (c_char_p * len(arguments))(*arguments)
execute = bind("mono_jit_exec", c_int, c_void_p, c_void_p, c_int, POINTER(c_char_p))
sys.exit(execute(domain, assembly, len(arguments), argv))
