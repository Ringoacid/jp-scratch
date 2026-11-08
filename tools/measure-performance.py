"""JP Scratch の状態別 CPU・メモリ計測。

既存の capture-docs-screenshots.py と同じく、外部 GUI ライブラリには依存せず
Win32 API でウィンドウを探して操作する。実データを変更しないよう、毎回
JPSCRATCH_DATA_DIR 配下の一時データで起動する。
"""

from __future__ import annotations

import argparse
import ctypes
import json
import os
import shutil
import subprocess
import tempfile
import time
from ctypes import wintypes
from pathlib import Path
from statistics import mean, median


user32 = ctypes.WinDLL("user32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
psapi = ctypes.WinDLL("psapi", use_last_error=True)

EnumWindowsProc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

user32.EnumWindows.argtypes = [EnumWindowsProc, wintypes.LPARAM]
user32.EnumWindows.restype = wintypes.BOOL
user32.IsWindowVisible.argtypes = [wintypes.HWND]
user32.IsWindowVisible.restype = wintypes.BOOL
user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
user32.GetWindowThreadProcessId.restype = wintypes.DWORD
user32.GetWindowTextLengthW.argtypes = [wintypes.HWND]
user32.GetWindowTextLengthW.restype = ctypes.c_int
user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user32.GetWindowTextW.restype = ctypes.c_int
user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
user32.GetWindowRect.restype = wintypes.BOOL
user32.GetDpiForWindow.argtypes = [wintypes.HWND]
user32.GetDpiForWindow.restype = wintypes.UINT
user32.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
user32.SetCursorPos.restype = wintypes.BOOL
user32.mouse_event.argtypes = [wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, ctypes.c_size_t]
user32.keybd_event.argtypes = [wintypes.BYTE, wintypes.BYTE, wintypes.DWORD, ctypes.c_size_t]
user32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
user32.PostMessageW.restype = wintypes.BOOL
user32.SetForegroundWindow.argtypes = [wintypes.HWND]
user32.SetForegroundWindow.restype = wintypes.BOOL
user32.AttachThreadInput.argtypes = [wintypes.DWORD, wintypes.DWORD, wintypes.BOOL]
user32.AttachThreadInput.restype = wintypes.BOOL
kernel32.GetCurrentThreadId.restype = wintypes.DWORD
kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
kernel32.OpenProcess.restype = wintypes.HANDLE
kernel32.GetProcessTimes.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p]
kernel32.GetProcessTimes.restype = wintypes.BOOL
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.CloseHandle.restype = wintypes.BOOL

PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
PROCESS_VM_READ = 0x0010
WM_CLOSE = 0x0010
MOUSEEVENTF_LEFTDOWN = 0x0002
MOUSEEVENTF_LEFTUP = 0x0004
KEYEVENTF_KEYUP = 0x0002

VK = {"ctrl": 0x11, "shift": 0x10, "alt": 0x12, "f": 0x46}


class PROCESS_MEMORY_COUNTERS_EX(ctypes.Structure):
    _fields_ = [
        ("cb", wintypes.DWORD),
        ("PageFaultCount", wintypes.DWORD),
        ("PeakWorkingSetSize", ctypes.c_size_t),
        ("WorkingSetSize", ctypes.c_size_t),
        ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
        ("QuotaPagedPoolUsage", ctypes.c_size_t),
        ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
        ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
        ("PagefileUsage", ctypes.c_size_t),
        ("PeakPagefileUsage", ctypes.c_size_t),
        ("PrivateUsage", ctypes.c_size_t),
    ]


class FILETIME(ctypes.Structure):
    _fields_ = [("dwLowDateTime", wintypes.DWORD), ("dwHighDateTime", wintypes.DWORD)]


psapi.GetProcessMemoryInfo.argtypes = [wintypes.HANDLE, ctypes.POINTER(PROCESS_MEMORY_COUNTERS_EX), wintypes.DWORD]
psapi.GetProcessMemoryInfo.restype = wintypes.BOOL


def window_text(hwnd: int) -> str:
    length = user32.GetWindowTextLengthW(hwnd)
    buffer = ctypes.create_unicode_buffer(length + 1)
    user32.GetWindowTextW(hwnd, buffer, len(buffer))
    return buffer.value


def window_pid(hwnd: int) -> int:
    pid = wintypes.DWORD()
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    return pid.value


def list_windows(pid: int) -> list[tuple[int, str]]:
    found: list[tuple[int, str]] = []

    @EnumWindowsProc
    def callback(hwnd: int, _lparam: int) -> bool:
        if user32.IsWindowVisible(hwnd) and window_pid(hwnd) == pid:
            found.append((hwnd, window_text(hwnd)))
        return True

    user32.EnumWindows(callback, 0)
    return found


def find_window(pid: int, title: str) -> int | None:
    return next((hwnd for hwnd, text in list_windows(pid) if text == title), None)


def find_window_containing(pid: int, text: str) -> int | None:
    return next((hwnd for hwnd, title in list_windows(pid) if text in title), None)


def wait_for(predicate, timeout: float, description: str):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        value = predicate()
        if value:
            return value
        time.sleep(0.05)
    raise RuntimeError(f"タイムアウト: {description}")


def focus(hwnd: int) -> None:
    target_thread = user32.GetWindowThreadProcessId(hwnd, None)
    current_thread = kernel32.GetCurrentThreadId()
    user32.AttachThreadInput(current_thread, target_thread, True)
    try:
        user32.SetForegroundWindow(hwnd)
    finally:
        user32.AttachThreadInput(current_thread, target_thread, False)
    time.sleep(0.15)


def window_rect(hwnd: int) -> wintypes.RECT:
    rect = wintypes.RECT()
    if not user32.GetWindowRect(hwnd, ctypes.byref(rect)):
        raise ctypes.WinError(ctypes.get_last_error())
    return rect


def click(hwnd: int, dip_x: float, dip_y: float) -> None:
    rect = window_rect(hwnd)
    dpi = user32.GetDpiForWindow(hwnd) or 96
    scale = dpi / 96
    user32.SetCursorPos(rect.left + int(dip_x * scale), rect.top + int(dip_y * scale))
    time.sleep(0.08)
    user32.mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
    user32.mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
    time.sleep(0.5)


def press(*keys: str) -> None:
    codes = [VK[key] for key in keys]
    for code in codes:
        user32.keybd_event(code, 0, 0, 0)
        time.sleep(0.03)
    for code in reversed(codes):
        user32.keybd_event(code, 0, KEYEVENTF_KEYUP, 0)
        time.sleep(0.03)
    time.sleep(0.25)


def close_window(hwnd: int) -> None:
    user32.PostMessageW(hwnd, WM_CLOSE, 0, 0)
    time.sleep(0.8)


def filetime_value(value: FILETIME) -> int:
    return (value.dwHighDateTime << 32) | value.dwLowDateTime


def process_snapshot(pid: int) -> dict[str, int]:
    handle = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, False, pid)
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        memory = PROCESS_MEMORY_COUNTERS_EX()
        memory.cb = ctypes.sizeof(memory)
        if not psapi.GetProcessMemoryInfo(handle, ctypes.byref(memory), memory.cb):
            raise ctypes.WinError(ctypes.get_last_error())

        creation = FILETIME()
        exit_time = FILETIME()
        kernel_time = FILETIME()
        user_time = FILETIME()
        if not kernel32.GetProcessTimes(handle, ctypes.byref(creation), ctypes.byref(exit_time), ctypes.byref(kernel_time), ctypes.byref(user_time)):
            raise ctypes.WinError(ctypes.get_last_error())

        return {
            "working_set_bytes": int(memory.WorkingSetSize),
            "private_bytes": int(memory.PrivateUsage),
            "cpu_100ns": filetime_value(kernel_time) + filetime_value(user_time),
        }
    finally:
        kernel32.CloseHandle(handle)


def sample_state(pid: int, seconds: float, interval: float) -> dict[str, object]:
    cpu_count = os.cpu_count() or 1
    samples: list[dict[str, float]] = []
    previous = process_snapshot(pid)
    previous_time = time.perf_counter()
    deadline = previous_time + seconds
    while time.perf_counter() < deadline:
        time.sleep(min(interval, max(0.0, deadline - time.perf_counter())))
        current = process_snapshot(pid)
        now = time.perf_counter()
        elapsed = max(now - previous_time, 1e-6)
        cpu = (current["cpu_100ns"] - previous["cpu_100ns"]) / 10_000_000 / elapsed / cpu_count * 100
        samples.append({
            "cpu_percent": max(0.0, cpu),
            "working_set_bytes": current["working_set_bytes"],
            "private_bytes": current["private_bytes"],
        })
        previous, previous_time = current, now

    def summary(key: str) -> dict[str, float]:
        values = [float(sample[key]) for sample in samples]
        return {
            "median": median(values),
            "average": mean(values),
            "minimum": min(values),
            "maximum": max(values),
        }

    return {
        "duration_seconds": seconds,
        "sample_count": len(samples),
        "cpu_percent": summary("cpu_percent"),
        "working_set_bytes": summary("working_set_bytes"),
        "private_bytes": summary("private_bytes"),
    }


def mb(value: float) -> float:
    return round(value / 1024 / 1024, 1)


def print_result(name: str, result: dict[str, object]) -> None:
    cpu = result["cpu_percent"]
    ws = result["working_set_bytes"]
    private = result["private_bytes"]
    print(
        f"{name}: CPU 中央値 {cpu['median']:.2f}% / 最大 {cpu['maximum']:.2f}%, "
        f"Working Set 中央値 {mb(ws['median']):.1f} MiB / 最大 {mb(ws['maximum']):.1f} MiB, "
        f"Private 中央値 {mb(private['median']):.1f} MiB / 最大 {mb(private['maximum']):.1f} MiB"
    )


def seed_settings(data_dir: Path) -> None:
    data_dir.mkdir(parents=True, exist_ok=True)
    settings = {
        "windowWidth": 620,
        "windowHeight": 620,
        "topmost": True,
        "hideOnFocusLost": False,
        "startWithWindows": False,
        "theme": "Light",
        "openAiApiKeySource": "EnvironmentVariable",
        "geminiApiKeySource": "EnvironmentVariable",
        "anthropicApiKeySource": "EnvironmentVariable",
        "plamoApiKeySource": "EnvironmentVariable",
        "autoProofreadingEnabled": False,
    }
    (data_dir / "settings.json").write_text(json.dumps(settings, ensure_ascii=False, indent=2), encoding="utf-8")


def ensure_no_app_running() -> None:
    running = subprocess.run(["tasklist", "/FI", "IMAGENAME eq JpScratch.exe", "/FO", "CSV", "/NH"], capture_output=True, text=True)
    if '"JpScratch.exe"' in running.stdout:
        raise RuntimeError("JpScratch.exe が既に起動しています。既存データ保護のため中止します。")


def show_existing(exe: Path, env: dict[str, str], pid: int) -> int:
    second = subprocess.Popen([str(exe)], env=env)
    second.wait(timeout=10)
    return wait_for(lambda: find_window(pid, "JP Scratch"), 10, "メイン画面の再表示")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--exe", type=Path, default=Path("publish/scd/JpScratch.exe"))
    parser.add_argument("--seconds", type=float, default=8.0)
    parser.add_argument("--interval", type=float, default=0.5)
    parser.add_argument("--out", type=Path, default=Path(".tmp/jpscratch-performance.json"))
    args = parser.parse_args()

    exe = args.exe.resolve()
    if not exe.exists():
        raise SystemExit(f"実行ファイルがありません: {exe}")
    ensure_no_app_running()

    data_dir = Path(tempfile.mkdtemp(prefix="JpScratch-Perf-"))
    env = dict(os.environ)
    env["JPSCRATCH_DATA_DIR"] = str(data_dir)
    process: subprocess.Popen | None = None
    results: dict[str, object] = {
        "executable": str(exe),
        "cpu_count": os.cpu_count() or 1,
        "sample_seconds": args.seconds,
        "sample_interval_seconds": args.interval,
        "states": {},
    }

    try:
        seed_settings(data_dir)
        process = subprocess.Popen([str(exe)], env=env)
        try:
            main_hwnd = wait_for(lambda: find_window(process.pid, "JP Scratch"), 20, "メイン画面")
        except Exception:
            print(
                f"起動診断: pid={process.pid}, poll={process.poll()}, "
                f"windows={list_windows(process.pid)}",
                flush=True,
            )
            raise
        focus(main_hwnd)
        time.sleep(1.5)

        state = sample_state(process.pid, args.seconds, args.interval)
        results["states"]["scratchpad_open"] = state
        print_result("スクラッチパッド表示", state)

        close_window(main_hwnd)
        wait_for(lambda: find_window(process.pid, "JP Scratch") is None, 10, "トレイ常駐への移行")
        time.sleep(2.0)
        state = sample_state(process.pid, args.seconds, args.interval)
        results["states"]["tray_resident"] = state
        print_result("トレイ常駐", state)

        main_hwnd = show_existing(exe, env, process.pid)
        focus(main_hwnd)
        click(main_hwnd, 620 - 45, 16)
        settings_hwnd = wait_for(lambda: find_window_containing(process.pid, "設定"), 10, "設定画面")
        focus(settings_hwnd)
        state = sample_state(process.pid, args.seconds, args.interval)
        results["states"]["settings"] = state
        print_result("設定画面", state)
        close_window(settings_hwnd)

        focus(main_hwnd)
        click(main_hwnd, 60, 620 - 12)
        billing_hwnd = wait_for(lambda: find_window_containing(process.pid, "課金履歴"), 10, "課金履歴画面")
        focus(billing_hwnd)
        state = sample_state(process.pid, args.seconds, args.interval)
        results["states"]["billing_history"] = state
        print_result("課金履歴画面", state)
        close_window(billing_hwnd)

        focus(main_hwnd)
        press("ctrl", "shift", "f")
        search_hwnd = wait_for(lambda: find_window_containing(process.pid, "全タブ検索"), 10, "全タブ検索画面")
        focus(search_hwnd)
        state = sample_state(process.pid, args.seconds, args.interval)
        results["states"]["cross_tab_search"] = state
        print_result("全タブ検索画面", state)
        close_window(search_hwnd)

        focus(main_hwnd)
        click(main_hwnd, 620 - 70, 16)
        trash_hwnd = wait_for(lambda: find_window_containing(process.pid, "ゴミ箱"), 10, "ゴミ箱画面")
        focus(trash_hwnd)
        state = sample_state(process.pid, args.seconds, args.interval)
        results["states"]["trash"] = state
        print_result("ゴミ箱画面", state)
        close_window(trash_hwnd)

        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"結果を保存しました: {args.out.resolve()}")
        return 0
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
        shutil.rmtree(data_dir, ignore_errors=True)


if __name__ == "__main__":
    raise SystemExit(main())
