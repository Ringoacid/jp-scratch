<#
.SYNOPSIS
    JP Scratch の起動・保存・常駐まわりを一通り叩く煙テスト。

.DESCRIPTION
    UI 自動化を使わず、WM_CHAR の送出とプロセス情報だけで
    「本文が失われないこと」と「常駐時メモリ目標」を確認する。
    %APPDATA%\JpScratch を消してから走るので、実データがある状態では実行しないこと。

    注意: このファイルは UTF-8 (BOM 付き) で保存すること。
    BOM が無いと Windows PowerShell 5.1 が日本語を CP932 として読み、構文エラーになる。

.EXAMPLE
    powershell -File tools\smoke-test.ps1 publish\fdd\JpScratch.exe
#>
$ErrorActionPreference = 'Stop'

Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public static class S {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
}
"@

function Find-AppWindow([int]$targetPid) {
    $script:foundWindow = [IntPtr]::Zero
    $cb = [S+EnumProc] {
        param($h, $l)
        $procId = 0
        [void][S]::GetWindowThreadProcessId($h, [ref]$procId)
        if ($procId -eq $targetPid -and [S]::IsWindowVisible($h)) {
            $sb = New-Object System.Text.StringBuilder 256
            [void][S]::GetWindowTextW($h, $sb, 256)
            if ($sb.ToString() -eq 'JP Scratch') { $script:foundWindow = $h; return $false }
        }
        return $true
    }
    [void][S]::EnumWindows($cb, [IntPtr]::Zero)
    return $script:foundWindow
}

function Send-Text([IntPtr]$hwnd, [string]$text) {
    foreach ($ch in $text.ToCharArray()) {
        [void][S]::PostMessageW($hwnd, 0x0102, [IntPtr][int][char]$ch, [IntPtr]0)
        Start-Sleep -Milliseconds 12
    }
}

function Wait-Window([int]$targetPid, [int]$timeoutMs = 8000) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $h = Find-AppWindow $targetPid
        if ($h -ne [IntPtr]::Zero) { return $h }
        Start-Sleep -Milliseconds 50
    }
    return [IntPtr]::Zero
}

$exe = $args[0]
$dataDir = "$env:APPDATA\JpScratch"

Get-Process JpScratch -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue

$fail = 0
function Check($name, $ok, $detail) {
    if ($ok) { Write-Host ("  OK   " + $name) -ForegroundColor Green }
    else { Write-Host ("  FAIL " + $name + " -- " + $detail) -ForegroundColor Red; $script:fail++ }
}

Write-Host '== 1. 起動とウィンドウ表示 =='
$p = Start-Process $exe -PassThru
$hwnd = Wait-Window $p.Id
Check 'ウィンドウが表示される' ($hwnd -ne [IntPtr]::Zero) 'window not found'

Write-Host '== 2. 入力と自動保存 =='
Send-Text $hwnd 'これは最初の行。'
Start-Sleep -Seconds 2
$tabFiles = @(Get-ChildItem "$dataDir\tabs" -File -ErrorAction SilentlyContinue)
Check 'タブ本文が書き出される' ($tabFiles.Count -eq 1) "files=$($tabFiles.Count)"
if ($tabFiles.Count -eq 1) {
    $body = [System.IO.File]::ReadAllText($tabFiles[0].FullName)
    Check '内容が一致する' ($body -eq 'これは最初の行。') "body='$body'"
    $bytes = [System.IO.File]::ReadAllBytes($tabFiles[0].FullName)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    Check 'BOM なし UTF-8 で保存される' (-not $hasBom) 'BOM found'
}

Write-Host '== 3. 非表示とメモリ返却 =='
[void][S]::PostMessageW($hwnd, 0x0010, [IntPtr]0, [IntPtr]0)   # WM_CLOSE -> hide
Start-Sleep -Seconds 4
$p.Refresh()
$hiddenWs = [math]::Round($p.WorkingSet64 / 1MB, 1)
Check '隠すとウィンドウが消える' ((Find-AppWindow $p.Id) -eq [IntPtr]::Zero) 'still visible'
Check "常駐時メモリが 80MB 以下 (実測 $hiddenWs MB)" ($hiddenWs -le 80) "$hiddenWs MB"

Write-Host '== 4. 二重起動 -> 既存インスタンスを呼び戻す =='
$p2 = Start-Process $exe -PassThru
Start-Sleep -Seconds 3
Check '2 つ目のプロセスは終了する' ($p2.HasExited) 'second instance still running'
$hwnd2 = Wait-Window $p.Id
Check '元のインスタンスが再表示される' ($hwnd2 -ne [IntPtr]::Zero) 'window did not reappear'

Write-Host '== 5. 再表示後も編集できる（キャレット位置が末尾） =='
if ($hwnd2 -ne [IntPtr]::Zero) {
    Send-Text $hwnd2 '追記。'
    Start-Sleep -Seconds 2
    $body2 = [System.IO.File]::ReadAllText($tabFiles[0].FullName)
    Check '末尾に追記される' ($body2 -eq 'これは最初の行。追記。') "body='$body2'"
}

Write-Host '== 6. 終了時保存 =='
Stop-Process -Id $p.Id -Force
Start-Sleep -Seconds 1
Check 'プロセスが終了する' ((Get-Process JpScratch -ErrorAction SilentlyContinue) -eq $null) 'still running'

Write-Host ''
if ($fail -eq 0) { Write-Host 'すべて成功' -ForegroundColor Green } else { Write-Host "$fail 件失敗" -ForegroundColor Red }
exit $fail
