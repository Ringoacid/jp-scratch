r"""JP Scratch の正規表現置換を実際のGUI操作で検証する。

外部GUI自動化ライブラリは使わず、Win32のキー入力だけで次を確認する。

1. 隔離データディレクトリでアプリを起動する（実データは触らない）
2. 本文を入力して Ctrl+F で検索パネルを開く
3. 正規表現を有効にし、Ctrl+H で置換欄へ移動する
4. キャプチャ参照と ``\n`` を含む置換パターンで「すべて置換」する
5. 自動保存された本文ファイルを読み、期待結果と一致することを確認する

Usage:
    python tools/gui-regex-replace-test.py
    python tools/gui-regex-replace-test.py path\to\JpScratch.exe

既定の実行ファイルは ``bin\Debug\net10.0-windows\JpScratch.exe``。
先に ``dotnet build .\jp-scratch.csproj`` を実行しておくこと。
"""

from __future__ import annotations

import argparse
import ctypes
import json
import os
import subprocess
import sys
import tempfile
import time
import winreg
from ctypes import wintypes
from pathlib import Path


if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")


user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

EnumWindowsProc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
EnumChildWindowsProc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

user32.EnumWindows.argtypes = [EnumWindowsProc, wintypes.LPARAM]
user32.EnumWindows.restype = wintypes.BOOL
user32.EnumChildWindows.argtypes = [wintypes.HWND, EnumChildWindowsProc, wintypes.LPARAM]
user32.EnumChildWindows.restype = wintypes.BOOL
user32.IsWindowVisible.argtypes = [wintypes.HWND]
user32.IsWindowVisible.restype = wintypes.BOOL
user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
user32.GetWindowThreadProcessId.restype = wintypes.DWORD
user32.GetWindowTextLengthW.argtypes = [wintypes.HWND]
user32.GetWindowTextLengthW.restype = ctypes.c_int
user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user32.GetWindowTextW.restype = ctypes.c_int
user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user32.GetClassNameW.restype = ctypes.c_int
user32.SetForegroundWindow.argtypes = [wintypes.HWND]
user32.SetForegroundWindow.restype = wintypes.BOOL
user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
user32.GetWindowRect.restype = wintypes.BOOL
user32.GetDpiForWindow.argtypes = [wintypes.HWND]
user32.GetDpiForWindow.restype = wintypes.UINT
user32.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
user32.SetCursorPos.restype = wintypes.BOOL
user32.mouse_event.argtypes = [
    wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, ctypes.c_size_t
]
user32.AttachThreadInput.argtypes = [wintypes.DWORD, wintypes.DWORD, wintypes.BOOL]
user32.AttachThreadInput.restype = wintypes.BOOL
user32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
user32.PostMessageW.restype = wintypes.BOOL
user32.keybd_event.argtypes = [wintypes.BYTE, wintypes.BYTE, wintypes.DWORD, ctypes.c_size_t]
kernel32.GetCurrentThreadId.restype = wintypes.DWORD

WM_CHAR = 0x0102
KEYEVENTF_KEYUP = 0x0002
MOUSEEVENTF_LEFTDOWN = 0x0002
MOUSEEVENTF_LEFTUP = 0x0004
VK = {
    "ctrl": 0x11,
    "shift": 0x10,
    "tab": 0x09,
    "space": 0x20,
}
for _letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ":
    VK[_letter.lower()] = ord(_letter)

RUN_KEY = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
RUN_VALUE = "JpScratch"
SOURCE_TEXT = "行1\n行2\n行3"
SEARCH_PATTERN = r"行(\d)\r?\n?"
REPLACEMENT_PATTERN = r"結果$1\n"
EXPECTED_TEXT = "結果1\n結果2\n結果3\n"


def window_text(hwnd: int) -> str:
    length = user32.GetWindowTextLengthW(hwnd)
    buffer = ctypes.create_unicode_buffer(length + 1)
    user32.GetWindowTextW(hwnd, buffer, len(buffer))
    return buffer.value


def class_name(hwnd: int) -> str:
    buffer = ctypes.create_unicode_buffer(256)
    user32.GetClassNameW(hwnd, buffer, len(buffer))
    return buffer.value


def window_pid(hwnd: int) -> int:
    pid = wintypes.DWORD()
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    return pid.value


def windows_for_pid(pid: int) -> list[int]:
    found: list[int] = []

    @EnumWindowsProc
    def callback(hwnd: int, _lparam: int) -> bool:
        if user32.IsWindowVisible(hwnd) and window_pid(hwnd) == pid:
            found.append(hwnd)
        return True

    user32.EnumWindows(callback, 0)
    return found


def find_window(pid: int, title: str) -> int | None:
    return next((hwnd for hwnd in windows_for_pid(pid) if window_text(hwnd) == title), None)


def dialog_details(hwnd: int) -> str:
    texts: list[str] = []

    @EnumChildWindowsProc
    def callback(child: int, _lparam: int) -> bool:
        text = window_text(child).strip()
        if text:
            texts.append(text)
        return True

    user32.EnumChildWindows(hwnd, callback, 0)
    return " / ".join(texts)


def assert_no_error_dialog(pid: int) -> None:
    errors = [
        f"{window_text(hwnd)}: {dialog_details(hwnd)}"
        for hwnd in windows_for_pid(pid)
        if class_name(hwnd) == "#32770"
    ]
    if errors:
        raise RuntimeError("エラーダイアログ: " + " / ".join(errors))


def wait_until(predicate, timeout: float, description: str):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        value = predicate()
        if value:
            return value
        time.sleep(0.05)
    raise RuntimeError(f"タイムアウト: {description}")


def wait_for_main_window(process: subprocess.Popen, timeout: float = 15) -> int:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        hwnd = find_window(process.pid, "JP Scratch")
        if hwnd:
            return hwnd
        if process.poll() is not None:
            raise RuntimeError(f"アプリがウィンドウ表示前に終了しました: exit={process.returncode}")
        time.sleep(0.05)

    visible = [
        f"{window_text(hwnd)!r} ({class_name(hwnd)})" for hwnd in windows_for_pid(process.pid)
    ]
    raise RuntimeError(
        "タイムアウト: メインウィンドウ; "
        f"process_running={process.poll() is None}, visible_windows={visible}"
    )


def focus(hwnd: int) -> None:
    target_thread = user32.GetWindowThreadProcessId(hwnd, None)
    current_thread = kernel32.GetCurrentThreadId()
    user32.AttachThreadInput(current_thread, target_thread, True)
    try:
        # Windowsは前面化要求を拒否してもGetLastErrorを設定しないことがある。
        # 入力キューを接続したうえで要求し、実際の入力成否を後段の結果検証で判定する。
        user32.SetForegroundWindow(hwnd)
    finally:
        user32.AttachThreadInput(current_thread, target_thread, False)
    time.sleep(0.2)


def press(*keys: str, hold: float = 0.04) -> None:
    codes = [VK[key] for key in keys]
    for code in codes:
        user32.keybd_event(code, 0, 0, 0)
        time.sleep(hold)
    for code in reversed(codes):
        user32.keybd_event(code, 0, KEYEVENTF_KEYUP, 0)
        time.sleep(hold)
    time.sleep(0.15)


def type_text(hwnd: int, text: str) -> None:
    for character in text:
        code = 0x0D if character == "\n" else ord(character)
        if not user32.PostMessageW(hwnd, WM_CHAR, wintypes.WPARAM(code), wintypes.LPARAM(0)):
            raise ctypes.WinError(ctypes.get_last_error())
        time.sleep(0.01)
    time.sleep(0.3)


def click(hwnd: int, dip_x: float, dip_y: float) -> None:
    rect = wintypes.RECT()
    if not user32.GetWindowRect(hwnd, ctypes.byref(rect)):
        raise ctypes.WinError(ctypes.get_last_error())
    dpi = user32.GetDpiForWindow(hwnd) or 96
    scale = dpi / 96
    if not user32.SetCursorPos(
        rect.left + int(dip_x * scale), rect.top + int(dip_y * scale)
    ):
        raise ctypes.WinError(ctypes.get_last_error())
    user32.mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    time.sleep(0.05)
    user32.mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    time.sleep(0.3)


def window_width_dip(hwnd: int) -> float:
    rect = wintypes.RECT()
    if not user32.GetWindowRect(hwnd, ctypes.byref(rect)):
        raise ctypes.WinError(ctypes.get_last_error())
    dpi = user32.GetDpiForWindow(hwnd) or 96
    return (rect.right - rect.left) / (dpi / 96)


def startup_is_registered() -> bool:
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY) as key:
            winreg.QueryValueEx(key, RUN_VALUE)
        return True
    except FileNotFoundError:
        return False


def seed_settings(data_dir: Path) -> None:
    data_dir.mkdir(parents=True, exist_ok=True)
    settings = {
        "hideOnFocusLost": False,
        "topmost": False,
        # 現在のレジストリ状態に合わせ、起動時の同期を実質no-opにする。
        "startWithWindows": startup_is_registered(),
        "windowWidth": 620,
        "windowHeight": 620,
        "positionMode": "TaskbarBottomRight",
        "theme": "Light",
        "autoProofreadingEnabled": False,
        "openAiApiKeySource": "EnvironmentVariable",
        "geminiApiKeySource": "EnvironmentVariable",
        "anthropicApiKeySource": "EnvironmentVariable",
        "plamoApiKeySource": "EnvironmentVariable",
    }
    (data_dir / "settings.json").write_text(
        json.dumps(settings, ensure_ascii=False, indent=2), encoding="utf-8"
    )


def normalize_newlines(value: str) -> str:
    return value.replace("\r\n", "\n").replace("\r", "\n")


def read_saved_text(data_dir: Path) -> str | None:
    tab_dir = data_dir / "tabs"
    if not tab_dir.exists():
        return None
    files = list(tab_dir.glob("*.txt"))
    if len(files) != 1:
        return None
    try:
        return files[0].read_text(encoding="utf-8")
    except (OSError, UnicodeError):
        # AtomicFileの差し替え直後など、一時的に共有違反になる間は再試行する。
        return None


def wait_for_saved_result(data_dir: Path, expected: str, timeout: float = 5) -> str:
    deadline = time.monotonic() + timeout
    latest: str | None = None
    while time.monotonic() < deadline:
        saved = read_saved_text(data_dir)
        if saved is not None:
            latest = normalize_newlines(saved)
            if latest == expected:
                return latest
        time.sleep(0.1)
    return latest or ""


def run_test(exe: Path) -> None:
    failure_screenshot = Path.cwd() / "gui-regex-replace-failure.png"
    failure_screenshot.unlink(missing_ok=True)
    with tempfile.TemporaryDirectory(prefix="jp-scratch-regex-replace-") as temp:
        data_dir = Path(temp)
        seed_settings(data_dir)
        env = dict(os.environ)
        env["JPSCRATCH_DATA_DIR"] = str(data_dir)
        process = subprocess.Popen([str(exe)], env=env)
        try:
            hwnd = wait_for_main_window(process)
            focus(hwnd)
            assert_no_error_dialog(process.pid)
            print("OK  隔離環境でメインウィンドウを起動")

            press("ctrl", "a")
            type_text(hwnd, SOURCE_TEXT)

            press("ctrl", "f")
            time.sleep(0.8)
            # キー入力先を検索欄へ固定する。前面化だけではWPF内のフォーカスが
            # エディタに残る環境があるため、既存の撮影スクリプトと同じ座標を使う。
            click(hwnd, 140, 87)
            type_text(hwnd, SEARCH_PATTERN)
            # 検索欄はウィンドウ幅に応じて伸びるため、右端基準のDIP座標で押す。
            width = window_width_dip(hwnd)
            click(hwnd, width - 307, 87)

            # 置換欄と「すべて置換」を直接操作し、Tab順への依存を避ける。
            click(hwnd, 140, 126)
            type_text(hwnd, REPLACEMENT_PATTERN)
            click(hwnd, width - 184, 126)
            assert_no_error_dialog(process.pid)
            print("OK  正規表現を有効化して『すべて置換』を実行")

            # 入力直後に保存された旧内容ではなく、置換後のデバウンス保存まで待つ。
            normalized = wait_for_saved_result(data_dir, EXPECTED_TEXT)
            if normalized != EXPECTED_TEXT:
                try:
                    from PIL import ImageGrab

                    rect = wintypes.RECT()
                    user32.GetWindowRect(hwnd, ctypes.byref(rect))
                    ImageGrab.grab(bbox=(rect.left, rect.top, rect.right, rect.bottom)).save(
                        failure_screenshot
                    )
                    print(f"診断画像: {failure_screenshot}", file=sys.stderr)
                except Exception as screenshot_error:
                    print(f"診断画像を保存できませんでした: {screenshot_error}", file=sys.stderr)
                raise RuntimeError(
                    "置換結果が異なります: "
                    f"expected={EXPECTED_TEXT!r}, actual={normalized!r}"
                )
            print("OK  $1 の参照と \\n の改行展開を保存結果で確認")
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()


def main() -> int:
    default_exe = Path(__file__).resolve().parents[1] / "bin/Debug/net10.0-windows/JpScratch.exe"
    parser = argparse.ArgumentParser()
    parser.add_argument("exe", nargs="?", type=Path, default=default_exe)
    args = parser.parse_args()
    exe = args.exe.resolve()
    if not exe.exists():
        print(f"実行ファイルがありません: {exe}", file=sys.stderr)
        return 2

    try:
        run_test(exe)
        print("PASS 正規表現置換GUIテスト")
        return 0
    except Exception as exc:
        print(f"FAIL {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
