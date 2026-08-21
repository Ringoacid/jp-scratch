"""README 用のスクリーンショットを一括で撮り直すスクリプト。

`JPSCRATCH_DATA_DIR` で隔離したデータディレクトリを作り、そこにデモ用のタブと設定を
仕込んでからアプリを起動する。実データ（%APPDATA%\\JpScratch）には一切触らない。

撮影は Win32 の PrintWindow(PW_RENDERFULLCONTENT) で「ウィンドウの枠だけ」を切り出す
（tools/screenshot-main.py と同じ方式）。操作はキーボード入力とマウスクリックの合成で行い、
UI 自動化ライブラリには依存しない（Pillow のみ使用）。

注意:
  * スタートアップ登録（HKCU\\...\\Run）はアプリ起動時に settings.json の値へ同期される。
    実データと同じレジストリを触るため、このスクリプトは起動前の値を退避し、終了時に必ず戻す。
  * `--shots proofreading` は実際に校正 API を呼ぶ（数円程度の課金が発生する）。
    既定のシナリオには含めていない。

Usage:
    python tools/capture-docs-screenshots.py                     # 課金なしの分だけ
    python tools/capture-docs-screenshots.py --shots all         # 校正・課金履歴も撮る（実課金）
    python tools/capture-docs-screenshots.py --shots main,dark
"""

from __future__ import annotations

import argparse
import ctypes
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import winreg
from ctypes import wintypes
from pathlib import Path

from PIL import Image

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
user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user32.GetClassNameW.restype = ctypes.c_int
user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
user32.GetWindowRect.restype = wintypes.BOOL
user32.GetDC.argtypes = [wintypes.HWND]
user32.GetDC.restype = wintypes.HDC
user32.ReleaseDC.argtypes = [wintypes.HWND, wintypes.HDC]
user32.ReleaseDC.restype = ctypes.c_int
user32.PrintWindow.argtypes = [wintypes.HWND, wintypes.HDC, wintypes.UINT]
user32.PrintWindow.restype = wintypes.BOOL
user32.SetForegroundWindow.argtypes = [wintypes.HWND]
user32.SetForegroundWindow.restype = wintypes.BOOL
user32.GetForegroundWindow.restype = wintypes.HWND
user32.AttachThreadInput.argtypes = [wintypes.DWORD, wintypes.DWORD, wintypes.BOOL]
user32.AttachThreadInput.restype = wintypes.BOOL
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
kernel32.GetCurrentThreadId.restype = wintypes.DWORD
user32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
user32.PostMessageW.restype = wintypes.BOOL
user32.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
user32.SetCursorPos.restype = wintypes.BOOL
user32.mouse_event.argtypes = [
    wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, ctypes.c_size_t
]
user32.keybd_event.argtypes = [wintypes.BYTE, wintypes.BYTE, wintypes.DWORD, ctypes.c_size_t]

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
WM_CHAR = 0x0102
MOUSEEVENTF_LEFTDOWN = 0x0002
MOUSEEVENTF_LEFTUP = 0x0004
MOUSEEVENTF_RIGHTDOWN = 0x0008
MOUSEEVENTF_RIGHTUP = 0x0010
KEYEVENTF_KEYUP = 0x0002

VK = {
    "ctrl": 0x11, "shift": 0x10, "alt": 0x12,
    "enter": 0x0D, "esc": 0x1B, "tab": 0x09, "back": 0x08,
    "left": 0x25, "up": 0x26, "right": 0x27, "down": 0x28,
    "home": 0x24, "end": 0x23,
    "f3": 0x72, "f8": 0x77,
    "period": 0xBE, "comma": 0xBC,
}
for _c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ":
    VK[_c.lower()] = ord(_c)

RUN_KEY = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
RUN_VALUE = "JpScratch"


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", wintypes.DWORD), ("biWidth", ctypes.c_long), ("biHeight", ctypes.c_long),
        ("biPlanes", wintypes.WORD), ("biBitCount", wintypes.WORD),
        ("biCompression", wintypes.DWORD), ("biSizeImage", wintypes.DWORD),
        ("biXPelsPerMeter", ctypes.c_long), ("biYPelsPerMeter", ctypes.c_long),
        ("biClrUsed", wintypes.DWORD), ("biClrImportant", wintypes.DWORD),
    ]


class BITMAPINFO(ctypes.Structure):
    _fields_ = [("bmiHeader", BITMAPINFOHEADER), ("bmiColors", wintypes.DWORD * 3)]


# ---------------------------------------------------------------------------
# ウィンドウ探索・撮影
# ---------------------------------------------------------------------------


def window_text(hwnd: int) -> str:
    length = user32.GetWindowTextLengthW(hwnd)
    buffer = ctypes.create_unicode_buffer(length + 1)
    user32.GetWindowTextW(hwnd, buffer, len(buffer))
    return buffer.value


def window_class(hwnd: int) -> str:
    buffer = ctypes.create_unicode_buffer(256)
    user32.GetClassNameW(hwnd, buffer, len(buffer))
    return buffer.value


def window_pid(hwnd: int) -> int:
    pid = wintypes.DWORD()
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    return pid.value


def list_windows(pid: int) -> list[tuple[int, str, str]]:
    found: list[tuple[int, str, str]] = []

    @EnumWindowsProc
    def callback(hwnd: int, _lparam: int) -> bool:
        if user32.IsWindowVisible(hwnd) and window_pid(hwnd) == pid:
            found.append((hwnd, window_text(hwnd), window_class(hwnd)))
        return True

    user32.EnumWindows(callback, 0)
    return found


def find_window(pid: int, title: str) -> int | None:
    for hwnd, text, _cls in list_windows(pid):
        if text == title:
            return hwnd
    return None


def wait_window(pid: int, title: str, timeout: float = 10.0) -> int:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        hwnd = find_window(pid, title)
        if hwnd:
            time.sleep(0.4)  # WPF のレイアウト確定を待つ
            return hwnd
        time.sleep(0.05)
    raise RuntimeError(f"タイムアウト: ウィンドウ「{title}」が見つかりません")


def _read_bitmap(mem_dc: int, bitmap: int, width: int, height: int) -> Image.Image:
    bmi = BITMAPINFO()
    bmi.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    bmi.bmiHeader.biWidth = width
    bmi.bmiHeader.biHeight = -height  # 負 = トップダウン
    bmi.bmiHeader.biPlanes = 1
    bmi.bmiHeader.biBitCount = 32
    bmi.bmiHeader.biCompression = 0
    buf = ctypes.create_string_buffer(width * height * 4)
    if gdi32.GetDIBits(mem_dc, bitmap, 0, height, buf, ctypes.byref(bmi), 0) == 0:
        raise ctypes.WinError(ctypes.get_last_error())
    return Image.frombuffer("RGB", (width, height), buf, "raw", "BGRX", 0, 1).copy()


def window_rect(hwnd: int) -> wintypes.RECT:
    rect = wintypes.RECT()
    if not user32.GetWindowRect(hwnd, ctypes.byref(rect)):
        raise ctypes.WinError(ctypes.get_last_error())
    return rect


def _capture(hwnd: int, use_bitblt: bool) -> Image.Image:
    rect = window_rect(hwnd)
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
            if use_bitblt:
                gdi32.BitBlt(mem_dc, 0, 0, width, height, screen_dc, rect.left, rect.top, SRCCOPY)
            elif not user32.PrintWindow(hwnd, mem_dc, PW_RENDERFULLCONTENT):
                user32.PrintWindow(hwnd, mem_dc, 0)
            gdi32.SelectObject(mem_dc, old_bitmap)
            return _read_bitmap(mem_dc, bitmap, width, height)
        finally:
            gdi32.DeleteObject(bitmap)
            gdi32.DeleteDC(mem_dc)
    finally:
        user32.ReleaseDC(None, screen_dc)


def is_blank(image: Image.Image) -> bool:
    colors = image.getcolors(maxcolors=2)
    return colors is not None and len(colors) <= 1


def capture_window(hwnd: int) -> Image.Image:
    """ウィンドウ単体。PrintWindow が空振りなら画面コピーへフォールバック。"""
    image = _capture(hwnd, use_bitblt=False)
    if is_blank(image):
        print("  注意: PrintWindow が空だったため BitBlt で再取得", file=sys.stderr)
        image = _capture(hwnd, use_bitblt=True)
    return image


# ---------------------------------------------------------------------------
# 入力の合成
# ---------------------------------------------------------------------------


def focus(hwnd: int) -> None:
    """対象ウィンドウを前面に出す。前面化の権利が無い場合に備えて入力キューを一時的に繋ぐ。"""
    target_thread = user32.GetWindowThreadProcessId(hwnd, None)
    current_thread = kernel32.GetCurrentThreadId()
    user32.AttachThreadInput(current_thread, target_thread, True)
    try:
        user32.SetForegroundWindow(hwnd)
    finally:
        user32.AttachThreadInput(current_thread, target_thread, False)
    time.sleep(0.15)


def close_window(hwnd: int) -> None:
    """WM_CLOSE で確実に閉じる。Esc を受け付けないウィンドウがあるため。"""
    user32.PostMessageW(hwnd, 0x0010, wintypes.WPARAM(0), wintypes.LPARAM(0))
    time.sleep(0.8)


def press(*keys: str, hold: float = 0.03) -> None:
    """修飾キー込みのキー入力。`press("ctrl", "f")` のように書く。"""
    codes = [VK[k] for k in keys]
    for code in codes:
        user32.keybd_event(code, 0, 0, 0)
        time.sleep(hold)
    for code in reversed(codes):
        user32.keybd_event(code, 0, KEYEVENTF_KEYUP, 0)
        time.sleep(hold)
    time.sleep(0.12)


def type_text(hwnd: int, text: str) -> None:
    """WM_CHAR でフォーカス中のコントロールへ文字を流し込む（IME を経由しない）。"""
    for ch in text:
        code = 0x0D if ch == "\n" else ord(ch)
        user32.PostMessageW(hwnd, WM_CHAR, wintypes.WPARAM(code), wintypes.LPARAM(0))
        time.sleep(0.004)
    time.sleep(0.3)


def click(hwnd: int, dip_x: float, dip_y: float, right: bool = False) -> None:
    """ウィンドウ左上からの DIP 座標でクリックする（DPI は実行時に取得して換算）。"""
    rect = window_rect(hwnd)
    try:
        dpi = user32.GetDpiForWindow(hwnd) or 96
    except AttributeError:
        dpi = 96
    scale = dpi / 96
    x = rect.left + int(dip_x * scale)
    y = rect.top + int(dip_y * scale)
    user32.SetCursorPos(x, y)
    time.sleep(0.08)
    down, up = (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP) if right \
        else (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP)
    user32.mouse_event(down, 0, 0, 0, 0)
    time.sleep(0.05)
    user32.mouse_event(up, 0, 0, 0, 0)
    time.sleep(0.35)


# ---------------------------------------------------------------------------
# デモ用データ
# ---------------------------------------------------------------------------

BASE_SETTINGS = {
    # 撮影中にフォーカスが外れても消えないようにする（既定は true）
    "hideOnFocusLost": False,
    "topmost": True,
    # レジストリを触らせない。終了時に元の値へ戻す処理も別途入れてある。
    "startWithWindows": False,
    "windowWidth": 620,
    "windowHeight": 620,
    "positionMode": "TaskbarBottomRight",
    "fontSize": 14,
    "theme": "Light",
    # 環境変数のキーをそのまま使う（初回起動時の取得元確認ダイアログを出さない）
    "openAiApiKeySource": "EnvironmentVariable",
    "geminiApiKeySource": "EnvironmentVariable",
    "plamoApiKeySource": "EnvironmentVariable",
    # 既定の手動用モデル（Anthropic）はキーが無い環境があるので OpenAI に寄せる
    "autoProofreadingModel": "gpt-5.6-luna",
    "manualProofreadingModel": "gpt-5.6-sol",
    "autoProofreadingEnabled": False,
}

TAB_MEETING = """定例ミーティング 2026-08-06

・v4 のプロバイダー拡張はマージ済み。自動用と手動用でモデルを分ける方式で確定。
・コンテキストキャッシュは Gemini の割引単価が未確認なので次回に持ち越す。
・インストーラーは自己完結版を既定の配布物にする。

次回までにやること
- README を一般ユーザー向けに書き直す
- スクリーンショットを撮り直す
"""

TAB_SNIPPET = """よく使うコマンド

wsl --shutdown
dotnet build
git switch -c feat/xxx

貼り付け用のパス
%APPDATA%\\JpScratch\\tabs
"""

TAB_DRAFT = """お問い合わせへの返信（下書き）

ご連絡ありがとうございます。
いただいた不具合は次のリリースで修正する予定です。
お手数をおかけしますが、今しばらくお待ちください。
"""

# 校正のデモ用。わざと誤字・重複表現を入れてある。
TAB_PROOFREAD = """新機能のお知らせ（校正前）

このたび、文章の校正機能を追加いたしまた。
入力を止めると自動的に校正が走り、気になる箇所に波線が引かれます。
提案は一つずつ確認できるので、意図しない書き換えが起るることはありません。
"""


def seed_data_dir(root: Path, settings_overrides: dict, tabs: list[tuple[str, str]]) -> None:
    """隔離データディレクトリに settings.json と本文ファイルを用意する。

    タブのメタ情報（順序・表示名）は app.db が持つが、DB を直接書くのは壊れやすい。
    ここでは本文ファイルだけを置き、タブの作成と改名はアプリの UI 操作で行う。
    """
    root.mkdir(parents=True, exist_ok=True)
    (root / "tabs").mkdir(exist_ok=True)
    settings = dict(BASE_SETTINGS)
    settings.update(settings_overrides)
    (root / "settings.json").write_text(
        json.dumps(settings, ensure_ascii=False, indent=2), encoding="utf-8"
    )


# ---------------------------------------------------------------------------
# アプリの起動と後始末
# ---------------------------------------------------------------------------


class AppSession:
    """隔離データディレクトリで JP Scratch を起動し、終了時に必ず後始末する。"""

    def __init__(self, exe: Path, data_dir: Path):
        self.exe = exe
        self.data_dir = data_dir
        self.process: subprocess.Popen | None = None
        self.hwnd = 0

    def __enter__(self) -> "AppSession":
        env = dict(os.environ)
        env["JPSCRATCH_DATA_DIR"] = str(self.data_dir)
        self.process = subprocess.Popen([str(self.exe)], env=env)
        try:
            self.hwnd = wait_window(self.process.pid, "JP Scratch", 20)
            focus(self.hwnd)
            time.sleep(0.6)
            return self
        except Exception:
            self._stop_owned_process()
            raise

    def __exit__(self, *_exc) -> None:
        self._stop_owned_process()
        time.sleep(0.5)

    def _stop_owned_process(self) -> None:
        """このセッションが起動したプロセスだけを終了する。"""
        if self.process and self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.process.kill()

    @property
    def pid(self) -> int:
        assert self.process is not None
        return self.process.pid

    def shot(self, out: Path, hwnd: int | None = None) -> None:
        image = capture_window(hwnd if hwnd is not None else self.hwnd)
        out.parent.mkdir(parents=True, exist_ok=True)
        image.save(out)
        print(f"  保存 {out.name}  ({image.width}x{image.height})")

    def wait_for(self, title: str, timeout: float = 15.0) -> int:
        return wait_window(self.pid, title, timeout)


def read_run_value() -> str | None:
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY) as key:
            return winreg.QueryValueEx(key, RUN_VALUE)[0]
    except FileNotFoundError:
        return None


def write_run_value(value: str | None) -> None:
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY, 0, winreg.KEY_SET_VALUE) as key:
        if value is None:
            try:
                winreg.DeleteValue(key, RUN_VALUE)
            except FileNotFoundError:
                pass
        else:
            winreg.SetValueEx(key, RUN_VALUE, 0, winreg.REG_SZ, value)


# ---------------------------------------------------------------------------
# 個々のシナリオ
# ---------------------------------------------------------------------------


def build_tabs(app: AppSession, tabs: list[tuple[str, str]]) -> None:
    """最初のタブへ書き込み、残りは Ctrl+T で足して本文を入れる。

    タブ名は付けない。本文の 1 行目から自動で付く見出しがそのまま実際の見え方になるので、
    デモ用に手で書き換えるより素の挙動を写したほうが正しい。
    """
    for index, (_title, body) in enumerate(tabs):
        if index > 0:
            press("ctrl", "t")
            time.sleep(0.6)
        type_text(app.hwnd, body)
        time.sleep(0.6)


def activate_first_tab(app: AppSession) -> None:
    """Ctrl+Tab は末尾から先頭へ回るので、最後に作ったタブから 1 回で先頭へ戻る。"""
    press("ctrl", "tab")
    time.sleep(0.5)
    press("ctrl", "home")


def scenario_main(app: AppSession, out: Path) -> None:
    activate_first_tab(app)
    app.shot(out / "main-light.png")


def scenario_find(app: AppSession, out: Path) -> None:
    press("ctrl", "f")
    time.sleep(1.0)
    # 検索欄を直接クリックしてから打つ。待ち時間だけに頼ると本文側へ文字が流れることがある。
    click(app.hwnd, 140, 87)
    type_text(app.hwnd, "モデル")
    time.sleep(0.6)
    app.shot(out / "find-replace.png")
    press("esc")
    time.sleep(0.4)


def scenario_cross_tab_search(app: AppSession, out: Path) -> None:
    press("ctrl", "shift", "f")
    hwnd = app.wait_for("全タブ検索")
    focus(hwnd)
    time.sleep(1.0)
    type_text(hwnd, "モデル")
    time.sleep(0.4)
    press("enter")
    time.sleep(1.5)
    app.shot(out / "cross-tab-search.png", hwnd)
    close_window(hwnd)
    focus(app.hwnd)


# 設定ウィンドウのタブ見出し（左から）と、その中心の X 座標（DIP）。
# TabControl は見出しに直接フォーカスが乗らないので、→ キーではなくクリックで切り替える。
SETTINGS_TABS = [
    ("general", 52),
    ("editor", 134),
    ("proofreading", 215),
    ("learning", 289),
    ("billing", 384),
]
SETTINGS_TAB_Y = 65


def scenario_settings(app: AppSession, out: Path) -> None:
    click(app.hwnd, BASE_SETTINGS["windowWidth"] - 45, 16)  # タイトルバーの歯車ボタン
    hwnd = app.wait_for("JP Scratch の設定")
    focus(hwnd)
    time.sleep(1.2)
    for name, x in SETTINGS_TABS:
        click(hwnd, x, SETTINGS_TAB_Y)
        time.sleep(0.8)
        app.shot(out / f"settings-{name}.png", hwnd)
    close_window(hwnd)
    focus(app.hwnd)


def scenario_context_menu(app: AppSession, out: Path) -> None:
    """右クリックで開くポップアップは別ウィンドウなので、開く前後の差分で特定する。"""
    before = {h for h, _t, _c in list_windows(app.pid)}
    click(app.hwnd, 200, 200, right=True)
    time.sleep(0.8)
    new = [h for h, _t, _c in list_windows(app.pid) if h not in before]
    if new:
        app.shot(out / "context-menu.png", new[-1])
    else:
        print("  警告: コンテキストメニューのウィンドウを見つけられませんでした", file=sys.stderr)
    press("esc")
    time.sleep(0.4)


def scenario_proofreading(app: AppSession, out: Path) -> None:
    # 選択はしない。選択中の青い反転が入ると、波線と提案パネルが読み取りにくくなる。
    press("ctrl", "home")
    press("ctrl", "enter")
    print("  校正 API の応答を待っています…")
    time.sleep(45)  # 手動用モデルの実測中央値（数秒）に対して十分な余裕を取る
    app.shot(out / "proofreading-suggestion.png")


def scenario_billing(app: AppSession, out: Path) -> None:
    # Ctrl+Shift+B でも開くが、長い待ちのあとはフォーカスが確実でない。
    # ステータスバー下段（課金表示）のクリックでも開くので、そちらを使う。
    focus(app.hwnd)
    time.sleep(0.4)
    click(app.hwnd, 60, BASE_SETTINGS["windowHeight"] - 12)
    hwnd = app.wait_for("課金履歴")
    focus(hwnd)
    time.sleep(1.2)
    app.shot(out / "billing-history.png", hwnd)
    press("esc")
    time.sleep(0.5)
    focus(app.hwnd)


# ---------------------------------------------------------------------------
# メイン
# ---------------------------------------------------------------------------

ALL_SHOTS = ["main", "find", "crosstab", "settings", "contextmenu", "dark", "proofreading"]
DEFAULT_SHOTS = ["main", "find", "crosstab", "settings", "contextmenu", "dark"]


def app_is_running() -> bool:
    """既存の JpScratch.exe があれば、誤入力を避けるため撮影を中止する。"""
    result = subprocess.run(
        ["tasklist", "/FI", "IMAGENAME eq JpScratch.exe", "/FO", "CSV", "/NH"],
        capture_output=True,
        text=True,
        check=False,
    )
    return '"JpScratch.exe"' in result.stdout


def main() -> int:
    parser = argparse.ArgumentParser(description="README 用スクショの一括撮影")
    parser.add_argument("--exe", type=Path, default=Path("publish/fdd/JpScratch.exe"))
    parser.add_argument("--out", type=Path, default=Path("docs/images"))
    parser.add_argument("--shots", default=",".join(DEFAULT_SHOTS),
                        help=f"撮る対象をカンマ区切りで。all で全部。候補: {','.join(ALL_SHOTS)}")
    parser.add_argument("--keep-data", action="store_true",
                        help="隔離データディレクトリを消さずに残す（デバッグ用）")
    args = parser.parse_args()

    exe = args.exe.resolve()
    if not exe.exists():
        print(f"実行ファイルがありません: {exe}", file=sys.stderr)
        return 2

    shots = ALL_SHOTS if args.shots == "all" else [s.strip() for s in args.shots.split(",") if s.strip()]
    unknown = [s for s in shots if s not in ALL_SHOTS]
    if unknown:
        print(f"不明な shot: {', '.join(unknown)}", file=sys.stderr)
        return 2

    out = args.out.resolve()
    out.mkdir(parents=True, exist_ok=True)

    if app_is_running():
        print(
            "JpScratch.exe が起動中です。未保存データを保護するため撮影を中止します。",
            file=sys.stderr,
        )
        return 3

    saved_run_value = read_run_value()
    workdir = Path(tempfile.mkdtemp(prefix="jpscratch-shots-"))
    print(f"隔離データディレクトリ: {workdir}")

    try:
        light_dir = workdir / "light"
        tabs = [("会議メモ", TAB_MEETING), ("よく使うもの", TAB_SNIPPET), ("返信の下書き", TAB_DRAFT)]

        if set(shots) & {"main", "find", "crosstab", "settings", "contextmenu"}:
            seed_data_dir(light_dir, {}, tabs)
            with AppSession(exe, light_dir) as app:
                build_tabs(app, tabs)
                # 検索系は最後に回す。検索語の入力が本文へ流れ込んだ場合でも
                # 先に撮った本文のスクショが汚れないようにするため。
                if "main" in shots:
                    scenario_main(app, out)
                if "contextmenu" in shots:
                    scenario_context_menu(app, out)
                if "settings" in shots:
                    scenario_settings(app, out)
                if "find" in shots:
                    scenario_find(app, out)
                if "crosstab" in shots:
                    scenario_cross_tab_search(app, out)

        if "dark" in shots:
            dark_dir = workdir / "dark"
            seed_data_dir(dark_dir, {"theme": "Dark"}, tabs)
            with AppSession(exe, dark_dir) as app:
                build_tabs(app, tabs)
                activate_first_tab(app)
                app.shot(out / "main-dark.png")

        if "proofreading" in shots:
            proof_dir = workdir / "proof"
            seed_data_dir(proof_dir, {"confirmPaidApiCalls": False}, [])
            with AppSession(exe, proof_dir) as app:
                type_text(app.hwnd, TAB_PROOFREAD)
                time.sleep(0.5)
                scenario_proofreading(app, out)
                scenario_billing(app, out)

        return 0
    finally:
        write_run_value(saved_run_value)
        print(f"スタートアップ登録を復元: {saved_run_value!r}")
        if args.keep_data:
            print(f"隔離データを残しました: {workdir}")
        else:
            shutil.rmtree(workdir, ignore_errors=True)


if __name__ == "__main__":
    raise SystemExit(main())
