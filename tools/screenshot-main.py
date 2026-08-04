"""JP Scratch のメインウィンドウ単体のスクリーンショットを撮るスクリプト。

画面全体ではなく「JP Scratch ウィンドウの枠だけ」を切り出して PNG に保存する。
Win32 API の PrintWindow(PW_RENDERFULLCONTENT) を使うため、他ウィンドウに
隠れていてもウィンドウ内容を撮れる。失敗時は BitBlt（画面コピー）にフォールバック。

既存の gui-settings-test.py と同じく外部 GUI ライブラリに依存しない
（Pillow のみ使用。Pillow はプロジェクトで導入済み）。

Usage:
    python tools/screenshot-main.py [--exe path\\to\\JpScratch.exe]
                                    [--out path\\to\\out.png]
                                    [--wait 秒]   # レンダリング待ち時間(既定 1.5)
"""

from __future__ import annotations

import argparse
import ctypes
import subprocess
import sys
import time
from ctypes import wintypes
from datetime import datetime
from pathlib import Path

from PIL import Image

# ---------------------------------------------------------------------------
# Win32 API 定義
# ---------------------------------------------------------------------------

# DPI 認識を有効化して物理ピクセルで扱う（WPF 側も物理ピクセル前提のため）。
try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)  # PROCESS_PER_MONITOR_DPI_AWARE
except Exception:
    ctypes.windll.user32.SetProcessDPIAware()

user32 = ctypes.WinDLL("user32", use_last_error=True)
gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)

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
user32.GetDC.argtypes = [wintypes.HWND]
user32.GetDC.restype = wintypes.HDC
user32.ReleaseDC.argtypes = [wintypes.HWND, wintypes.HDC]
user32.ReleaseDC.restype = ctypes.c_int
user32.PrintWindow.argtypes = [wintypes.HWND, wintypes.HDC, wintypes.UINT]
user32.PrintWindow.restype = wintypes.BOOL

gdi32.CreateCompatibleDC.argtypes = [wintypes.HDC]
gdi32.CreateCompatibleDC.restype = wintypes.HDC
gdi32.CreateCompatibleBitmap.argtypes = [wintypes.HDC, ctypes.c_int, ctypes.c_int]
gdi32.CreateCompatibleBitmap.restype = wintypes.HBITMAP
gdi32.SelectObject.argtypes = [wintypes.HDC, wintypes.HGDIOBJ]
gdi32.SelectObject.restype = wintypes.HGDIOBJ
gdi32.BitBlt.argtypes = [
    wintypes.HDC, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
    wintypes.HDC, ctypes.c_int, ctypes.c_int, wintypes.DWORD,
]
gdi32.BitBlt.restype = wintypes.BOOL
gdi32.GetDIBits.argtypes = [
    wintypes.HDC, wintypes.HBITMAP, wintypes.UINT, wintypes.UINT,
    ctypes.c_void_p, ctypes.c_void_p, wintypes.UINT,
]
gdi32.GetDIBits.restype = ctypes.c_int
gdi32.DeleteObject.argtypes = [wintypes.HGDIOBJ]
gdi32.DeleteObject.restype = wintypes.BOOL
gdi32.DeleteDC.argtypes = [wintypes.HDC]
gdi32.DeleteDC.restype = wintypes.BOOL

SRCCOPY = 0x00CC0020
PW_RENDERFULLCONTENT = 0x00000002


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", wintypes.DWORD),
        ("biWidth", ctypes.c_long),
        ("biHeight", ctypes.c_long),
        ("biPlanes", wintypes.WORD),
        ("biBitCount", wintypes.WORD),
        ("biCompression", wintypes.DWORD),
        ("biSizeImage", wintypes.DWORD),
        ("biXPelsPerMeter", ctypes.c_long),
        ("biYPelsPerMeter", ctypes.c_long),
        ("biClrUsed", wintypes.DWORD),
        ("biClrImportant", wintypes.DWORD),
    ]


class BITMAPINFO(ctypes.Structure):
    _fields_ = [
        ("bmiHeader", BITMAPINFOHEADER),
        ("bmiColors", wintypes.DWORD * 3),
    ]


# ---------------------------------------------------------------------------
# ヘルパー
# ---------------------------------------------------------------------------


def window_text(hwnd: int) -> str:
    length = user32.GetWindowTextLengthW(hwnd)
    buffer = ctypes.create_unicode_buffer(length + 1)
    user32.GetWindowTextW(hwnd, buffer, len(buffer))
    return buffer.value


def window_pid(hwnd: int) -> int:
    pid = wintypes.DWORD()
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    return pid.value


def find_window(pid: int, title: str) -> int | None:
    """指定 PID に属する、タイトルが一致する可視ウィンドウを 1 つ返す。"""
    found: list[int] = []

    @EnumWindowsProc
    def callback(hwnd: int, _lparam: int) -> bool:
        if user32.IsWindowVisible(hwnd) and window_pid(hwnd) == pid and window_text(hwnd) == title:
            found.append(hwnd)
            return False
        return True

    user32.EnumWindows(callback, 0)
    return found[0] if found else None


def wait_window(pid: int, title: str, timeout: float) -> int:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        hwnd = find_window(pid, title)
        if hwnd:
            return hwnd
        time.sleep(0.05)
    raise RuntimeError(f"タイムアウト: ウィンドウ「{title}」が見つかりません")


def _read_bitmap(mem_dc: int, bitmap: int, width: int, height: int) -> Image.Image:
    """GDI ビットマップのピクセルを GetDIBits で読み出して PIL イメージにする。"""
    bmi = BITMAPINFO()
    bmi.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    bmi.bmiHeader.biWidth = width
    bmi.bmiHeader.biHeight = -height  # 負 = トップダウン（上から下の順）
    bmi.bmiHeader.biPlanes = 1
    bmi.bmiHeader.biBitCount = 32
    bmi.bmiHeader.biCompression = 0  # BI_RGB

    buf = ctypes.create_string_buffer(width * height * 4)
    got = gdi32.GetDIBits(mem_dc, bitmap, 0, height, buf, ctypes.byref(bmi), 0)
    if got == 0:
        raise ctypes.WinError(ctypes.get_last_error())

    img = Image.frombuffer("RGB", (width, height), buf, "raw", "BGRX", 0, 1)
    return img.copy()  # frombuffer はバッファを共有するため必ずコピー


def _window_size(hwnd: int) -> tuple[int, int]:
    rect = wintypes.RECT()
    if not user32.GetWindowRect(hwnd, ctypes.byref(rect)):
        raise ctypes.WinError(ctypes.get_last_error())
    return rect.right - rect.left, rect.bottom - rect.top


def capture_print_window(hwnd: int) -> Image.Image:
    """PrintWindow(PW_RENDERFULLCONTENT) でウィンドウ単体を撮る。"""
    width, height = _window_size(hwnd)
    if width <= 0 or height <= 0:
        raise RuntimeError(f"ウィンドウサイズが不正です: {width}x{height}")

    screen_dc = user32.GetDC(None)
    if not screen_dc:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        mem_dc = gdi32.CreateCompatibleDC(screen_dc)
        bitmap = gdi32.CreateCompatibleBitmap(screen_dc, width, height)
        if not bitmap:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            old_bitmap = gdi32.SelectObject(mem_dc, bitmap)
            if not user32.PrintWindow(hwnd, mem_dc, PW_RENDERFULLCONTENT):
                # 失敗時は通常モードで再試行（稀に PW_RENDERFULLCONTENT が通らない環境がある）
                user32.PrintWindow(hwnd, mem_dc, 0)
            gdi32.SelectObject(mem_dc, old_bitmap)
            return _read_bitmap(mem_dc, bitmap, width, height)
        finally:
            gdi32.DeleteObject(bitmap)
            gdi32.DeleteDC(mem_dc)
    finally:
        user32.ReleaseDC(None, screen_dc)


def capture_bitblt(hwnd: int) -> Image.Image:
    """BitBlt で画面からウィンドウ領域を切り出す（PrintWindow 失敗時のフォールバック）。"""
    rect = wintypes.RECT()
    if not user32.GetWindowRect(hwnd, ctypes.byref(rect)):
        raise ctypes.WinError(ctypes.get_last_error())
    width, height = rect.right - rect.left, rect.bottom - rect.top
    if width <= 0 or height <= 0:
        raise RuntimeError(f"ウィンドウサイズが不正です: {width}x{height}")

    screen_dc = user32.GetDC(None)
    if not screen_dc:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        mem_dc = gdi32.CreateCompatibleDC(screen_dc)
        bitmap = gdi32.CreateCompatibleBitmap(screen_dc, width, height)
        if not bitmap:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            old_bitmap = gdi32.SelectObject(mem_dc, bitmap)
            gdi32.BitBlt(mem_dc, 0, 0, width, height, screen_dc, rect.left, rect.top, SRCCOPY)
            gdi32.SelectObject(mem_dc, old_bitmap)
            return _read_bitmap(mem_dc, bitmap, width, height)
        finally:
            gdi32.DeleteObject(bitmap)
            gdi32.DeleteDC(mem_dc)
    finally:
        user32.ReleaseDC(None, screen_dc)


def is_blank(image: Image.Image) -> bool:
    """画像が単一色（真っ黒など）かどうか。PrintWindow の空振り判定に使う。"""
    colors = image.getcolors(maxcolors=2)
    if colors is None:
        return False  # 2 色を超える色がある = 空ではない
    return len(colors) <= 1


def capture_window(hwnd: int) -> Image.Image:
    """ウィンドウ単体のスクショ。PrintWindow が空振りなら BitBlt にフォールバック。"""
    image = capture_print_window(hwnd)
    if is_blank(image):
        print("  注意: PrintWindow の結果が空のため BitBlt で再取得します", file=sys.stderr)
        image = capture_bitblt(hwnd)
    return image


# ---------------------------------------------------------------------------
# メイン
# ---------------------------------------------------------------------------


def main() -> int:
    parser = argparse.ArgumentParser(description="JP Scratch メインウィンドウのスクショを撮る")
    parser.add_argument("--exe", type=Path, default=Path("publish/fdd/JpScratch.exe"))
    parser.add_argument("--out", type=Path, default=None, help="出力 PNG パス（既定: screenshots/auto/main-<時刻>.png）")
    parser.add_argument("--wait", type=float, default=1.5, help="ウィンドウ表示後のレンダリング待ち秒数")
    args = parser.parse_args()

    exe = args.exe.resolve()
    if not exe.exists():
        print(f"実行ファイルがありません: {exe}", file=sys.stderr)
        return 2

    # 既存インスタンスを終了（単一インスタンスアプリのため）
    subprocess.run(["taskkill", "/IM", "JpScratch.exe", "/F"], capture_output=True)
    time.sleep(0.8)

    print(f"起動: {exe}")
    process = subprocess.Popen([str(exe)])
    try:
        hwnd = wait_window(process.pid, "JP Scratch", 10)
        print("OK  メインウィンドウを検出")
        time.sleep(args.wait)  # WPF のレンダリング完了を待つ

        image = capture_window(hwnd)
        width, height = image.size
        print(f"OK  キャプチャ成功: {width}x{height}px")

        out = args.out or Path("screenshots/auto") / f"main-{datetime.now():%Y%m%d-%H%M%S}.png"
        out = out.resolve()
        out.parent.mkdir(parents=True, exist_ok=True)
        image.save(out)
        print(f"保存: {out}")
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
