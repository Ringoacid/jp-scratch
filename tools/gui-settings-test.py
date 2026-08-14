"""JP Scratch の設定画面・終了処理を確認する最小GUIスモークテスト。

外部GUIライブラリに依存せず、Win32 APIだけで次を確認する。
1. メインウィンドウを起動できる
2. ゴミ箱ボタンをクリックしてゴミ箱ウィンドウを開ける
3. ゴミ箱ウィンドウを閉じられる
4. 設定ボタンをクリックして設定ウィンドウを開ける
5. 設定ウィンドウを閉じられる（閉じる時の保存処理を含む）
6. テスト専用終了フラグで通常の Application.OnExit まで到達できる

Usage:
    python tools/gui-settings-test.py path\\to\\JpScratch.exe
"""

from __future__ import annotations

import argparse
import ctypes
import subprocess
import sys
import time
from ctypes import wintypes
from pathlib import Path


user32 = ctypes.WinDLL("user32", use_last_error=True)

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
user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
user32.GetWindowRect.restype = wintypes.BOOL
user32.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
user32.SetCursorPos.restype = wintypes.BOOL
user32.mouse_event.argtypes = [wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, ctypes.c_size_t]
user32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
user32.PostMessageW.restype = wintypes.BOOL

WM_CLOSE = 0x0010
MOUSEEVENTF_LEFTDOWN = 0x0002
MOUSEEVENTF_LEFTUP = 0x0004


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


def error_dialogs(pid: int) -> list[str]:
    errors: list[str] = []
    for hwnd in windows_for_pid(pid):
        if class_name(hwnd) == "#32770":
            errors.append(f"{window_text(hwnd)}: {dialog_details(hwnd)}")
    return errors


def find_window(pid: int, title: str) -> int | None:
    for hwnd in windows_for_pid(pid):
        if window_text(hwnd) == title:
            return hwnd
    return None


def wait_until(predicate, timeout: float, description: str):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        value = predicate()
        if value:
            return value
        time.sleep(0.05)
    raise RuntimeError(f"タイムアウト: {description}")


def click_settings_button(main_hwnd: int) -> None:
    rect = wintypes.RECT()
    if not user32.GetWindowRect(main_hwnd, ctypes.byref(rect)):
        raise ctypes.WinError(ctypes.get_last_error())

    # タイトルバー右側は「ピン留め・ゴミ箱・設定・非表示」の順で、各ボタンは28px。
    # 設定ボタンの中心は右端から 14 + 28 = 42px。
    x = rect.right - 42
    y = rect.top + 16
    user32.SetCursorPos(x, y)
    user32.mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    user32.mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)


def click_trash_button(main_hwnd: int) -> None:
    rect = wintypes.RECT()
    if not user32.GetWindowRect(main_hwnd, ctypes.byref(rect)):
        raise ctypes.WinError(ctypes.get_last_error())

    # ゴミ箱ボタンは設定ボタンの左隣。中心は右端から 14 + 28 + 28 = 70px。
    x = rect.right - 70
    y = rect.top + 16
    user32.SetCursorPos(x, y)
    user32.mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    user32.mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)


def close_window(hwnd: int) -> None:
    if not user32.PostMessageW(hwnd, WM_CLOSE, 0, 0):
        raise ctypes.WinError(ctypes.get_last_error())


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("exe", type=Path)
    args = parser.parse_args()
    exe = args.exe.resolve()
    if not exe.exists():
        print(f"実行ファイルがありません: {exe}", file=sys.stderr)
        return 2

    process = subprocess.Popen([str(exe), "--gui-test-exit-after-settings"])
    try:
        main_hwnd = wait_until(
            lambda: find_window(process.pid, "JP Scratch"),
            10,
            "メインウィンドウ",
        )
        if error_dialogs(process.pid):
            raise RuntimeError("起動時エラーダイアログ: " + " / ".join(error_dialogs(process.pid)))
        print("OK  メインウィンドウが表示されました")

        click_trash_button(main_hwnd)
        trash_hwnd = wait_until(
            lambda: find_window(process.pid, "ゴミ箱"),
            10,
            "ゴミ箱ウィンドウ",
        )
        if error_dialogs(process.pid):
            raise RuntimeError("ゴミ箱画面のエラーダイアログ: " + " / ".join(error_dialogs(process.pid)))
        print("OK  ゴミ箱ウィンドウを開けました")

        close_window(trash_hwnd)
        wait_until(
            lambda: find_window(process.pid, "ゴミ箱") is None,
            10,
            "ゴミ箱ウィンドウの終了",
        )
        if error_dialogs(process.pid):
            raise RuntimeError("ゴミ箱画面のエラーダイアログ: " + " / ".join(error_dialogs(process.pid)))
        print("OK  ゴミ箱ウィンドウを閉じられました")

        click_settings_button(main_hwnd)
        settings_hwnd = wait_until(
            lambda: find_window(process.pid, "JP Scratch の設定"),
            10,
            "設定ウィンドウ",
        )
        if error_dialogs(process.pid):
            raise RuntimeError("設定画面のエラーダイアログ: " + " / ".join(error_dialogs(process.pid)))
        print("OK  設定ウィンドウを開けました")

        close_window(settings_hwnd)
        wait_until(
            lambda: find_window(process.pid, "JP Scratch の設定") is None,
            10,
            "設定ウィンドウの終了",
        )
        if error_dialogs(process.pid):
            raise RuntimeError("設定保存時のエラーダイアログ: " + " / ".join(error_dialogs(process.pid)))
        print("OK  設定ウィンドウを閉じられました")

        process.wait(timeout=10)
        if process.returncode != 0:
            raise RuntimeError(f"アプリ終了コードが異常です: {process.returncode}")
        print("OK  アプリが正常終了しました")
        return 0
    except Exception as exc:
        print(f"FAIL {exc}", file=sys.stderr)
        return 1
    finally:
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                process.kill()


if __name__ == "__main__":
    raise SystemExit(main())
