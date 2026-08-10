"""
Will's VIP 1132 User Manager
Neon Cyberpunk Edition — PyQt6
Run with: py -3.12 vip_1132_manager.py
Requires:  pip install PyQt6
Must be run as Administrator for net user commands to work.
"""

import sys
import os
import json
import socket
import subprocess
import zipfile
import urllib.request
import re
from datetime import datetime

from PyQt6.QtWidgets import (
    QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
    QLabel, QPushButton, QTextEdit, QDialog, QLineEdit,
    QScrollArea, QFrame, QSizePolicy
)
from PyQt6.QtCore import Qt, QThread, pyqtSignal, QTimer
from PyQt6.QtGui import (
    QFont, QColor, QPainter, QPen,
    QRadialGradient, QPalette,
    QTextCharFormat, QTextCursor
)

# ── CONSTANTS ────────────────────────────────────────────────────────────────

CLEANZOOM_URL    = "https://assets.zoom.us/docs/msi-templates/CleanZoom.zip"
ZOOM_MSI_URL     = "https://zoom.us/client/latest/ZoomInstallerFull.msi?archType=x64"
PUBLIC_DOWNLOADS = r"C:\Users\Public\Downloads"
CLEANZOOM_ZIP    = os.path.join(PUBLIC_DOWNLOADS, "CleanZoom.zip")
CLEANZOOM_EXE    = os.path.join(PUBLIC_DOWNLOADS, "CleanZoom.exe")
ZOOM_MSI         = os.path.join(PUBLIC_DOWNLOADS, "ZoomInstallerFull.msi")

# State file lives next to the script so it persists across runs
STATE_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "vip_state.json")

# ── COLORS ───────────────────────────────────────────────────────────────────

C_CYAN    = "#00f5ff"
C_PINK    = "#ff00c8"
C_PURPLE  = "#bf00ff"
C_GREEN   = "#00ff88"
C_ORANGE  = "#ff6b00"
C_RED     = "#ff4444"
C_GOLD    = "#ffd700"
C_BG_DEEP = "#050812"
C_BG_PAN  = "#080f1e"
C_BG_CARD = "#0c1529"
C_TEXT    = "#e0f0ff"
C_MUTED   = "#6a8aaa"
C_DIM     = "#3a5060"

# ── STATE MANAGER ─────────────────────────────────────────────────────────────

class StateManager:
    """
    Persists current_user_num across app restarts via a JSON file.
    user1  password: 1
    user2  password: 2
    user3  password: 3  ... and so on forever
    """
    def __init__(self):
        self._data = {"current_user_num": None}
        self._load()

    def _load(self):
        try:
            if os.path.exists(STATE_FILE):
                with open(STATE_FILE, "r") as f:
                    self._data = json.load(f)
        except Exception:
            self._data = {"current_user_num": None}

    def _save(self):
        try:
            with open(STATE_FILE, "w") as f:
                json.dump(self._data, f)
        except Exception:
            pass

    @property
    def current_user_num(self):
        return self._data.get("current_user_num")

    @current_user_num.setter
    def current_user_num(self, val):
        self._data["current_user_num"] = val
        self._save()

    def next_user_num(self):
        cur = self.current_user_num
        return 1 if cur is None else cur + 1

    def current_username(self):
        n = self.current_user_num
        return str(n) if n else None

    def next_username(self):
        return str(self.next_user_num())

    def advance(self):
        """Commit next number as current and save."""
        self.current_user_num = self.next_user_num()

    def clear(self):
        self.current_user_num = None


STATE = StateManager()


# ── WORKER THREAD ────────────────────────────────────────────────────────────

class Worker(QThread):
    log_signal   = pyqtSignal(str, str)
    done_signal  = pyqtSignal(str)
    error_signal = pyqtSignal(str)

    def __init__(self, task, **kwargs):
        super().__init__()
        self.task   = task
        self.kwargs = kwargs

    def run(self):
        try:
            getattr(self, f"task_{self.task}")(**self.kwargs)
        except Exception as e:
            self.error_signal.emit(str(e))

    # ── helpers ──────────────────────────────────────────────────────────────

    def _run_cmd(self, cmd):
        r = subprocess.run(cmd, shell=True, capture_output=True, text=True)
        return r.returncode, r.stdout.strip(), r.stderr.strip()

    def _download(self, url, dest, label):
        self.log_signal.emit(f"Fetching: {url}", "data")
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        last = [-1]

        def prog(count, block, total):
            if total > 0:
                pct = min(int(count * block * 100 / total), 100)
                if pct % 25 == 0 and pct != last[0]:
                    last[0] = pct
                    self.log_signal.emit(f"  {label}: {pct}%...", "data")

        urllib.request.urlretrieve(url, dest, reporthook=prog)
        self.log_signal.emit(f"Saved: {dest}", "data")

    def _kill_zoom(self):
        self.log_signal.emit("Checking if Zoom is running...", "data")
        rc, out, _ = self._run_cmd('tasklist /FI "IMAGENAME eq Zoom.exe" /NH')
        if "Zoom.exe" in out:
            self.log_signal.emit("Zoom process detected — force killing...", "warn")
            self._run_cmd("taskkill /F /IM Zoom.exe /T")
            self._run_cmd("taskkill /F /IM CptHost.exe /T")
            self._run_cmd("taskkill /F /IM zCrashReport64.exe /T")
            import time; time.sleep(1)
            _, out2, _ = self._run_cmd('tasklist /FI "IMAGENAME eq Zoom.exe" /NH')
            if "Zoom.exe" in out2:
                self.log_signal.emit("WARNING: Zoom still running. CleanZoom may fail.", "warn")
            else:
                self.log_signal.emit("Zoom process killed successfully.", "success")
        else:
            self.log_signal.emit("Zoom is not running. Safe to proceed.", "success")

    def _do_cleanzoom(self):
        self.log_signal.emit("Checking for CleanZoom.exe...", "data")
        if os.path.exists(CLEANZOOM_EXE):
            self.log_signal.emit("CleanZoom detected locally. Skipping download.", "success")
        else:
            self.log_signal.emit("Downloading CleanZoom from Zoom servers...", "warn")
            self._download(CLEANZOOM_URL, CLEANZOOM_ZIP, "CleanZoom.zip")
            self.log_signal.emit("Extracting CleanZoom.zip...", "data")
            with zipfile.ZipFile(CLEANZOOM_ZIP, "r") as zf:
                zf.extractall(PUBLIC_DOWNLOADS)
            self.log_signal.emit("CleanZoom extracted.", "success")

        self.log_signal.emit("Running CleanZoom.exe /silent...", "warn")
        self.log_signal.emit("Purging all Zoom files, services & registry entries...", "warn")
        rc, _, _ = self._run_cmd(f'"{CLEANZOOM_EXE}" /silent')
        if rc not in (0, 1):
            self.log_signal.emit(f"WARNING: CleanZoom exited code {rc}. Continuing anyway.", "warn")
        else:
            self.log_signal.emit("Zoom uninstalled successfully.", "success")

    # ── individual tasks ─────────────────────────────────────────────────────

    def task_create_user(self, username, password):
        self.log_signal.emit(f'net user "{username}" "{password}" /add /active:yes', "data")
        rc, out, err = self._run_cmd(f'net user "{username}" "{password}" /add /active:yes')
        if rc != 0:
            self.log_signal.emit(f"ERROR: {err or out}", "error")
            self.done_signal.emit("create_fail")
            return
        self.log_signal.emit(f"User '{username}' created. Password = '{password}'.", "success")
        rc2, _, err2 = self._run_cmd(f'net localgroup Administrators "{username}" /add')
        if rc2 != 0:
            self.log_signal.emit(f"WARNING (admin group): {err2}", "warn")
        else:
            self.log_signal.emit(f"'{username}' added to Administrators.", "success")
        self.done_signal.emit(f"create_ok:{username}")

    def task_delete_user(self, username):
        self.log_signal.emit(f'net user "{username}" /delete', "data")
        rc, out, err = self._run_cmd(f'net user "{username}" /delete')
        if rc != 0:
            self.log_signal.emit(f"ERROR: {err or out}", "error")
            self.done_signal.emit("delete_fail")
            return
        self.log_signal.emit(f"User '{username}' wiped from system.", "warn")
        self.done_signal.emit(f"delete_ok:{username}")

    def task_list_users(self):
        self.log_signal.emit(f"net user  (\\\\{socket.gethostname()})", "data")
        rc, out, err = self._run_cmd("net user")
        if rc != 0:
            self.log_signal.emit(f"ERROR: {err}", "error")
            self.done_signal.emit("list_fail")
            return
        users, capture = [], False
        for line in out.splitlines():
            if line.startswith("---"):
                capture = True
                continue
            if capture and line.strip() and not line.startswith("The command"):
                users += [u.strip() for u in line.split() if u.strip()]
        self.done_signal.emit("list_ok:" + ",".join(users))

    def task_uninstall_zoom(self):
        self._kill_zoom()
        self._do_cleanzoom()
        self.done_signal.emit("uninstall_ok")

    def task_download_zoom(self):
        self._download(ZOOM_MSI_URL, ZOOM_MSI, "ZoomInstallerFull.msi")
        self.log_signal.emit("Zoom installer saved to C:\\Users\\Public\\Downloads.", "success")
        self.log_signal.emit(
            "SHIFT+Right-click .msi → Run as different user → enter credentials.", "info"
        )
        self.done_signal.emit("download_ok")

    def task_open_downloads(self):
        self.log_signal.emit(f"Opening: {PUBLIC_DOWNLOADS}", "info")
        subprocess.Popen(f'explorer "{PUBLIC_DOWNLOADS}"')
        self.log_signal.emit("Explorer launched at C:\\Users\\Public\\Downloads", "success")
        self.done_signal.emit("open_ok")

    def task_full_sequence(self, old_username, new_username, new_password):
        import time

        self.log_signal.emit("━━━  STEP 1 / 5  —  KILLING ZOOM  ━━━", "info")
        self._kill_zoom()
        time.sleep(0.5)

        self.log_signal.emit("━━━  STEP 2 / 5  —  UNINSTALLING ZOOM  ━━━", "info")
        self._do_cleanzoom()
        time.sleep(0.5)

        self.log_signal.emit("━━━  STEP 3 / 5  —  WIPING PREVIOUS USER  ━━━", "info")
        if old_username:
            self.log_signal.emit(f'Deleting user: "{old_username}"...', "warn")
            rc, out, err = self._run_cmd(f'net user "{old_username}" /delete')
            if rc != 0:
                self.log_signal.emit(f"WARNING: Could not delete '{old_username}': {err or out}", "warn")
            else:
                self.log_signal.emit(f"User '{old_username}' wiped from system.", "success")
        else:
            self.log_signal.emit("No previous user to wipe. Skipping.", "data")
        time.sleep(0.3)

        self.log_signal.emit("━━━  STEP 4 / 5  —  CREATING NEW USER  ━━━", "info")
        self.log_signal.emit(
            f'net user "{new_username}" "{new_password}" /add /active:yes', "data"
        )
        rc3, out3, err3 = self._run_cmd(
            f'net user "{new_username}" "{new_password}" /add /active:yes'
        )
        if rc3 != 0:
            self.log_signal.emit(f"ERROR creating user: {err3 or out3}", "error")
            self.done_signal.emit("sequence_fail")
            return
        self.log_signal.emit(
            f"User '{new_username}' created. Password = '{new_password}'.", "success"
        )
        rc4, _, err4 = self._run_cmd(f'net localgroup Administrators "{new_username}" /add')
        if rc4 != 0:
            self.log_signal.emit(f"WARNING (admin group): {err4}", "warn")
        else:
            self.log_signal.emit(f"'{new_username}' added to Administrators.", "success")
        time.sleep(0.3)

        self.log_signal.emit("━━━  STEP 5 / 6  —  DOWNLOADING ZOOM  ━━━", "info")
        self._download(ZOOM_MSI_URL, ZOOM_MSI, "ZoomInstallerFull.msi")
        self.log_signal.emit("Zoom installer saved to C:\\Users\\Public\\Downloads.", "success")
        time.sleep(0.3)

        self.log_signal.emit("━━━  STEP 6 / 6  —  INSTALLING ZOOM AS NEW USER  ━━━", "info")
        self.log_signal.emit(f"Scheduling Zoom install to run as user '{new_username}'...", "data")

        machine = socket.gethostname()
        task_name = "VIP_ZoomInstall"
        msi_path  = ZOOM_MSI.replace("'", "''")

        # Delete any leftover task from previous run
        self._run_cmd(f'schtasks /delete /tn "{task_name}" /f')

        # Create a scheduled task that runs msiexec as the new user
        # ALLUSERS=0 = install for that user only (key for 1132 bypass)
        create_cmd = (
            f'schtasks /create /tn "{task_name}" /f '
            f'/ru "{machine}\\{new_username}" /rp "{new_password}" '
            f'/sc once /st 00:00 '
            f'/tr "msiexec /i \\"{ZOOM_MSI}\\" /qn /norestart ALLUSERS=0"'
        )
        rc_c, out_c, err_c = self._run_cmd(create_cmd)
        if rc_c != 0:
            self.log_signal.emit(f"ERROR creating scheduled task: {err_c or out_c}", "error")
            self.log_signal.emit("Fallback: manually SHIFT+Right-click the MSI → Run as different user.", "warn")
            self.log_signal.emit(f"Username: {new_username}     Password: {new_password}", "info")
            self.done_signal.emit(f"sequence_ok:{new_username}")
            return

        self.log_signal.emit("Task created. Running now...", "data")
        rc_r, out_r, err_r = self._run_cmd(f'schtasks /run /tn "{task_name}"')
        if rc_r != 0:
            self.log_signal.emit(f"ERROR running task: {err_r or out_r}", "error")
            self.log_signal.emit("Fallback: manually SHIFT+Right-click the MSI → Run as different user.", "warn")
            self.log_signal.emit(f"Username: {new_username}     Password: {new_password}", "info")
            self.done_signal.emit(f"sequence_ok:{new_username}")
            return

        # Wait for msiexec to finish (poll task status)
        self.log_signal.emit("Installing Zoom... please wait (this may take 30-60 seconds).", "warn")
        import time
        for _ in range(90):          # max ~90 seconds
            time.sleep(1)
            _, status_out, _ = self._run_cmd(
                f'schtasks /query /tn "{task_name}" /fo LIST'
            )
            if "Running" not in status_out:
                break

        # Check if Zoom actually installed
        zoom_installed = os.path.exists(
            os.path.join(os.environ.get("SystemDrive", "C:"),
                         "Users", new_username, "AppData", "Roaming", "Zoom", "bin", "Zoom.exe")
        )

        # Clean up the task
        self._run_cmd(f'schtasks /delete /tn "{task_name}" /f')

        if zoom_installed:
            self.log_signal.emit("Zoom installed successfully under the new user!", "success")
        else:
            self.log_signal.emit("Install task ran — Zoom.exe not detected yet (may still be finishing).", "warn")
            self.log_signal.emit("Check C:\\Users\\{}\\AppData\\Roaming\\Zoom\\bin\\".format(new_username), "data")

        # ── LAUNCH ZOOM as the new user ───────────────────────────────────────
        zoom_exe = os.path.join(
            os.environ.get("SystemDrive", "C:"),
            "Users", new_username, "AppData", "Roaming", "Zoom", "bin", "Zoom.exe"
        )
        self.log_signal.emit("Launching Zoom as new user...", "info")

        launch_task = "VIP_ZoomLaunch"
        self._run_cmd(f'schtasks /delete /tn "{launch_task}" /f')

        rc_l, out_l, err_l = self._run_cmd(
            f'schtasks /create /tn "{launch_task}" /f '
            f'/ru "{machine}\\{new_username}" /rp "{new_password}" '
            f'/sc once /st 00:00 '
            f'/tr "\\"{zoom_exe}\\""'
        )
        if rc_l != 0:
            self.log_signal.emit(f"Could not schedule Zoom launch: {err_l or out_l}", "warn")
            self.log_signal.emit("Open Zoom manually: SHIFT+Right-click shortcut → Run as different user.", "warn")
        else:
            self._run_cmd(f'schtasks /run /tn "{launch_task}"')
            time.sleep(2)
            self._run_cmd(f'schtasks /delete /tn "{launch_task}" /f')
            self.log_signal.emit("Zoom launched as the new user!", "success")

        self.log_signal.emit("━━━  ALL DONE  ━━━", "success")
        self.log_signal.emit(
            "Every time you open Zoom: SHIFT+Right-click shortcut → Run as different user", "info"
        )
        self.log_signal.emit(f"Username: {new_username}     Password: {new_password}", "info")
        self.done_signal.emit(f"sequence_ok:{new_username}")


# ── ANIMATED BACKGROUND ───────────────────────────────────────────────────────

class BackgroundWidget(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self._phase = 0.0
        t = QTimer(self)
        t.timeout.connect(self._tick)
        t.start(50)

    def _tick(self):
        self._phase = (self._phase + 0.008) % 1.0
        self.update()

    def paintEvent(self, event):
        import math
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.fillRect(self.rect(), QColor(C_BG_DEEP))

        pen = QPen(QColor(0, 245, 255, 8))
        pen.setWidth(1)
        p.setPen(pen)
        for x in range(0, self.width(), 40):
            p.drawLine(x, 0, x, self.height())
        for y in range(0, self.height(), 40):
            p.drawLine(0, y, self.width(), y)

        a = int(28 + 22 * math.sin(self._phase * 2 * math.pi))
        g1 = QRadialGradient(self.width() * 0.2, self.height() * 0.2, self.width() * 0.5)
        g1.setColorAt(0, QColor(191, 0, 255, a))
        g1.setColorAt(1, QColor(0, 0, 0, 0))
        p.fillRect(self.rect(), g1)

        g2 = QRadialGradient(self.width() * 0.8, self.height() * 0.8, self.width() * 0.5)
        g2.setColorAt(0, QColor(0, 245, 255, a))
        g2.setColorAt(1, QColor(0, 0, 0, 0))
        p.fillRect(self.rect(), g2)
        p.end()


# ── NEON BUTTON ───────────────────────────────────────────────────────────────

class NeonButton(QPushButton):
    def __init__(self, text, color_hex, parent=None):
        super().__init__(text, parent)
        self._color = QColor(color_hex)
        self._glow  = 0.3
        self._hover = False
        self._anim  = QTimer(self)
        self._anim.timeout.connect(self._tick)
        self.setFixedHeight(38)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self.setFont(QFont("Segoe UI", 9, QFont.Weight.Bold))
        self._refresh()

    def _refresh(self):
        r, g, b = self._color.red(), self._color.green(), self._color.blue()
        bg_a  = int(self._glow * 0.15 * 255)
        brd_a = int((0.25 + self._glow * 0.75) * 255)
        self.setStyleSheet(f"""
            QPushButton {{
                background-color: rgba({r},{g},{b},{bg_a});
                color: rgb({r},{g},{b});
                border: 1px solid rgba({r},{g},{b},{brd_a});
                border-radius: 5px;
                padding: 8px 16px;
                letter-spacing: 1px;
            }}
            QPushButton:disabled {{
                color: rgba({r},{g},{b},80);
                border: 1px solid rgba({r},{g},{b},40);
                background-color: transparent;
            }}
        """)

    def enterEvent(self, e):
        self._hover = True
        self._anim.start(16)
        super().enterEvent(e)

    def leaveEvent(self, e):
        self._hover = False
        super().leaveEvent(e)

    def _tick(self):
        self._glow = max(0.3, min(1.0, self._glow + (0.08 if self._hover else -0.06)))
        self._refresh()
        if (self._hover and self._glow >= 1.0) or (not self._hover and self._glow <= 0.3):
            self._anim.stop()

    def paintEvent(self, event):
        super().paintEvent(event)
        if self._glow > 0.4:
            p = QPainter(self)
            p.setRenderHint(QPainter.RenderHint.Antialiasing)
            c = self._color
            a = int((self._glow - 0.4) * 140)
            pen = QPen(QColor(c.red(), c.green(), c.blue(), a))
            pen.setWidth(3)
            p.setPen(pen)
            p.setBrush(Qt.BrushStyle.NoBrush)
            p.drawRoundedRect(self.rect().adjusted(1, 1, -1, -1), 5, 5)
            p.end()


# ── INPUT DIALOG ─────────────────────────────────────────────────────────────

class NeonDialog(QDialog):
    def __init__(self, title, label, confirm_text="Confirm", accent=C_CYAN, parent=None):
        super().__init__(parent)
        self.setModal(True)
        self.setFixedWidth(360)
        self.setWindowFlags(
            Qt.WindowType.Dialog |
            Qt.WindowType.FramelessWindowHint |
            Qt.WindowType.WindowStaysOnTopHint
        )
        self.setStyleSheet(f"""
            QDialog {{
                background-color:{C_BG_CARD};
                border:1px solid rgba(0,245,255,0.35);
                border-radius:8px;
            }}
        """)
        lay = QVBoxLayout(self)
        lay.setContentsMargins(28, 24, 28, 24)
        lay.setSpacing(14)

        line = QFrame()
        line.setFixedHeight(1)
        line.setStyleSheet(
            f"background:qlineargradient(x1:0,y1:0,x2:1,y2:0,"
            f"stop:0 transparent,stop:0.5 {accent},stop:1 transparent);"
        )
        lay.addWidget(line)

        QLabel_style = f"color:{accent}; letter-spacing:2px;"
        t = QLabel(title.upper())
        t.setFont(QFont("Consolas", 11, QFont.Weight.Bold))
        t.setStyleSheet(QLabel_style)
        lay.addWidget(t)

        lbl = QLabel(label.upper())
        lbl.setFont(QFont("Segoe UI", 8))
        lbl.setStyleSheet(f"color:{C_MUTED}; letter-spacing:2px;")
        lay.addWidget(lbl)

        self.input = QLineEdit()
        self.input.setFixedHeight(38)
        self.input.setFont(QFont("Consolas", 12))
        self.input.setStyleSheet(f"""
            QLineEdit {{
                background:{C_BG_DEEP}; color:{accent};
                border:1px solid rgba(0,245,255,0.35);
                border-radius:4px; padding:0 12px; letter-spacing:1px;
            }}
            QLineEdit:focus {{ border:1px solid {accent}; }}
        """)
        self.input.returnPressed.connect(self.accept)
        lay.addWidget(self.input)

        row = QHBoxLayout()
        row.setSpacing(10)
        ok = NeonButton(confirm_text, accent)
        ok.clicked.connect(self.accept)
        row.addWidget(ok)
        ca = NeonButton("Cancel", C_MUTED)
        ca.clicked.connect(self.reject)
        row.addWidget(ca)
        lay.addLayout(row)

    def get_value(self):
        return self.input.text().strip()

    def exec(self):
        self.input.setFocus()
        return super().exec()


# ── USERS LIST DIALOG ─────────────────────────────────────────────────────────

def _hex_rgb(h):
    h = h.lstrip("#")
    return f"{int(h[0:2],16)},{int(h[2:4],16)},{int(h[4:6],16)}"


class UsersDialog(QDialog):
    def __init__(self, users, current_zoom_user, parent=None):
        super().__init__(parent)
        self.setModal(True)
        self.setFixedWidth(440)
        self.setWindowFlags(
            Qt.WindowType.Dialog |
            Qt.WindowType.FramelessWindowHint |
            Qt.WindowType.WindowStaysOnTopHint
        )
        self.setStyleSheet(f"""
            QDialog {{
                background-color:{C_BG_CARD};
                border:1px solid rgba(0,245,255,0.35);
                border-radius:8px;
            }}
        """)
        lay = QVBoxLayout(self)
        lay.setContentsMargins(24, 24, 24, 24)
        lay.setSpacing(12)

        line = QFrame()
        line.setFixedHeight(1)
        line.setStyleSheet(
            f"background:qlineargradient(x1:0,y1:0,x2:1,y2:0,"
            f"stop:0 transparent,stop:0.5 {C_CYAN},stop:1 transparent);"
        )
        lay.addWidget(line)

        title = QLabel(f"≡  SYSTEM USERS — {socket.gethostname().upper()}")
        title.setFont(QFont("Consolas", 10, QFont.Weight.Bold))
        title.setStyleSheet(f"color:{C_CYAN}; letter-spacing:2px;")
        lay.addWidget(title)

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setMaximumHeight(340)
        scroll.setStyleSheet(f"""
            QScrollArea {{ border:none; background:transparent; }}
            QScrollBar:vertical {{ background:{C_BG_DEEP}; width:4px; border-radius:2px; }}
            QScrollBar::handle:vertical {{ background:rgba(0,245,255,0.2); border-radius:2px; }}
        """)

        inner = QWidget()
        inner.setStyleSheet("background:transparent;")
        vbox = QVBoxLayout(inner)
        vbox.setSpacing(6)
        vbox.setContentsMargins(0, 0, 0, 0)

        SYSTEM_ACCTS = {
            "administrator", "defaultaccount", "guest",
            "wdagutilityaccount", "wsiaccount"
        }
        vip_re = re.compile(r'^\d+$')

        for u in users:
            is_system = u.lower() in SYSTEM_ACCTS
            is_vip    = bool(vip_re.match(u))
            is_active = (u.lower() == (current_zoom_user or "").lower())

            if is_active:
                tag_color, tag_text = C_GOLD,   "zoom · active"
            elif is_vip:
                tag_color, tag_text = C_GREEN,  "zoom · admin"
            else:
                tag_color, tag_text = C_ORANGE, "system"

            row = QFrame()
            bc = C_GOLD if is_active else "rgba(0,245,255,0.1)"
            row.setStyleSheet(f"""
                QFrame {{
                    background:rgba(0,245,255,0.03);
                    border:1px solid {bc};
                    border-radius:4px;
                }}
                QFrame:hover {{
                    border:1px solid rgba(0,245,255,0.25);
                    background:rgba(0,245,255,0.05);
                }}
            """)
            rlay = QHBoxLayout(row)
            rlay.setContentsMargins(12, 8, 12, 8)
            rlay.setSpacing(10)

            initials = QLabel(u[:2].upper())
            initials.setFixedSize(32, 32)
            initials.setAlignment(Qt.AlignmentFlag.AlignCenter)
            initials.setFont(QFont("Consolas", 10, QFont.Weight.Bold))
            initials.setStyleSheet(f"""
                color:{C_CYAN}; border:1px solid rgba(0,245,255,0.3);
                border-radius:16px; background:rgba(0,245,255,0.05);
            """)
            rlay.addWidget(initials)

            name_lbl = QLabel(u)
            name_lbl.setFont(QFont("Consolas", 11))
            name_lbl.setStyleSheet(f"color:{C_TEXT}; border:none; background:transparent;")
            rlay.addWidget(name_lbl, 1)

            tag = QLabel(tag_text)
            tag.setFont(QFont("Segoe UI", 8))
            tag.setStyleSheet(f"""
                color:{tag_color}; background:transparent;
                border:1px solid rgba({_hex_rgb(tag_color)},0.3);
                border-radius:2px; padding:2px 8px; letter-spacing:1px;
            """)
            rlay.addWidget(tag)
            vbox.addWidget(row)

        vbox.addStretch()
        scroll.setWidget(inner)
        lay.addWidget(scroll)

        NeonButton("Close", C_MUTED, self).clicked.connect(self.accept)
        lay.addWidget(self.findChild(NeonButton))

    # simpler: just add close btn directly
    def _close(self):
        self.accept()


class UsersDialog(QDialog):
    def __init__(self, users, current_zoom_user, parent=None):
        super().__init__(parent)
        self.setModal(True)
        self.setFixedWidth(440)
        self.setWindowFlags(
            Qt.WindowType.Dialog |
            Qt.WindowType.FramelessWindowHint |
            Qt.WindowType.WindowStaysOnTopHint
        )
        self.setStyleSheet(f"QDialog{{background-color:{C_BG_CARD};border:1px solid rgba(0,245,255,0.35);border-radius:8px;}}")
        lay = QVBoxLayout(self)
        lay.setContentsMargins(24, 24, 24, 24)
        lay.setSpacing(12)

        line = QFrame()
        line.setFixedHeight(1)
        line.setStyleSheet(f"background:qlineargradient(x1:0,y1:0,x2:1,y2:0,stop:0 transparent,stop:0.5 {C_CYAN},stop:1 transparent);")
        lay.addWidget(line)

        title = QLabel(f"≡  SYSTEM USERS — {socket.gethostname().upper()}")
        title.setFont(QFont("Consolas", 10, QFont.Weight.Bold))
        title.setStyleSheet(f"color:{C_CYAN}; letter-spacing:2px;")
        lay.addWidget(title)

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setMaximumHeight(340)
        scroll.setStyleSheet(f"QScrollArea{{border:none;background:transparent;}}QScrollBar:vertical{{background:{C_BG_DEEP};width:4px;border-radius:2px;}}QScrollBar::handle:vertical{{background:rgba(0,245,255,0.2);border-radius:2px;}}")

        inner = QWidget()
        inner.setStyleSheet("background:transparent;")
        vbox = QVBoxLayout(inner)
        vbox.setSpacing(6)
        vbox.setContentsMargins(0, 0, 0, 0)

        SYSTEM_ACCTS = {"administrator","defaultaccount","guest","wdagutilityaccount","wsiaccount"}
        vip_re = re.compile(r'^\d+$')

        for u in users:
            is_active = (u.lower() == (current_zoom_user or "").lower())
            is_vip    = bool(vip_re.match(u))
            is_system = u.lower() in SYSTEM_ACCTS

            if is_active:
                tc, tt = C_GOLD,   "zoom · active"
            elif is_vip:
                tc, tt = C_GREEN,  "zoom · admin"
            else:
                tc, tt = C_ORANGE, "system"

            row = QFrame()
            row.setStyleSheet(f"QFrame{{background:rgba(0,245,255,0.03);border:1px solid {'#ffd700' if is_active else 'rgba(0,245,255,0.1)'};border-radius:4px;}}QFrame:hover{{border:1px solid rgba(0,245,255,0.25);background:rgba(0,245,255,0.05);}}")
            rl = QHBoxLayout(row)
            rl.setContentsMargins(12, 8, 12, 8)
            rl.setSpacing(10)

            ini = QLabel(u[:2].upper())
            ini.setFixedSize(32, 32)
            ini.setAlignment(Qt.AlignmentFlag.AlignCenter)
            ini.setFont(QFont("Consolas", 10, QFont.Weight.Bold))
            ini.setStyleSheet(f"color:{C_CYAN};border:1px solid rgba(0,245,255,0.3);border-radius:16px;background:rgba(0,245,255,0.05);")
            rl.addWidget(ini)

            nl = QLabel(u)
            nl.setFont(QFont("Consolas", 11))
            nl.setStyleSheet(f"color:{C_TEXT};border:none;background:transparent;")
            rl.addWidget(nl, 1)

            tg = QLabel(tt)
            tg.setFont(QFont("Segoe UI", 8))
            tg.setStyleSheet(f"color:{tc};background:transparent;border:1px solid rgba({_hex_rgb(tc)},0.3);border-radius:2px;padding:2px 8px;letter-spacing:1px;")
            rl.addWidget(tg)
            vbox.addWidget(row)

        vbox.addStretch()
        scroll.setWidget(inner)
        lay.addWidget(scroll)

        cb = NeonButton("Close", C_MUTED)
        cb.clicked.connect(self.accept)
        lay.addWidget(cb)


# ── INSTRUCTIONS DIALOG ───────────────────────────────────────────────────────

class InstructionsDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setModal(True)
        self.setFixedWidth(480)
        self.setWindowFlags(
            Qt.WindowType.Dialog |
            Qt.WindowType.FramelessWindowHint |
            Qt.WindowType.WindowStaysOnTopHint
        )
        self.setStyleSheet(f"QDialog{{background-color:{C_BG_CARD};border:1px solid rgba(0,245,255,0.35);border-radius:8px;}}")
        lay = QVBoxLayout(self)
        lay.setContentsMargins(28, 28, 28, 28)
        lay.setSpacing(14)

        line = QFrame()
        line.setFixedHeight(1)
        line.setStyleSheet(f"background:qlineargradient(x1:0,y1:0,x2:1,y2:0,stop:0 transparent,stop:0.5 {C_CYAN},stop:1 transparent);")
        lay.addWidget(line)

        QLabel("ⓘ  INSTALLATION PROTOCOL", font=QFont("Consolas", 11, QFont.Weight.Bold)).setParent(self)
        t = QLabel("ⓘ  INSTALLATION PROTOCOL")
        t.setFont(QFont("Consolas", 11, QFont.Weight.Bold))
        t.setStyleSheet(f"color:{C_CYAN}; letter-spacing:2px;")
        lay.addWidget(t)

        steps = [
            ("1", "Uninstall Zoom",
             "Click  ⊘ Uninstall Zoom\nThis removes Zoom completely using CleanZoom.zip from Zoom's site."),
            ("2", "Create New User",
             "Click  ◈ Create New User\nYou'll be asked for a new username.\nThe password will automatically be the same as the username."),
            ("3", "Download Zoom",
             "Click  ↓ Download Zoom\nIt saves the installer in C:\\Users\\Public\\Downloads"),
            ("4", "INSTALL ZOOM AS THE NEW USER (IMPORTANT!)",
             "⚠ HOLD SHIFT and Right-click on the Zoom installer .msi\nSelect 'Run as different user'\nEnter the new username + password you just created."),
            ("5", "EVERY TIME YOU WANT TO USE ZOOM, YOU MUST DO THIS:",
             "⚠ HOLD SHIFT + Right-click on the Zoom shortcut\nSelect 'Run as different user'\nEnter that same username & password"),
        ]

        for num, heading, body in steps:
            sf = QFrame()
            sf.setStyleSheet("QFrame{border:none;background:transparent;}")
            sl = QHBoxLayout(sf)
            sl.setContentsMargins(0, 0, 0, 0)
            sl.setSpacing(12)
            sl.setAlignment(Qt.AlignmentFlag.AlignTop)

            nl = QLabel(num)
            nl.setFixedSize(22, 22)
            nl.setAlignment(Qt.AlignmentFlag.AlignCenter)
            nl.setFont(QFont("Consolas", 9, QFont.Weight.Bold))
            nl.setStyleSheet(f"color:{C_CYAN};border:1px solid rgba(0,245,255,0.4);border-radius:11px;background:rgba(0,245,255,0.05);")
            sl.addWidget(nl, 0, Qt.AlignmentFlag.AlignTop)

            tb = QVBoxLayout()
            tb.setSpacing(2)
            hl = QLabel(heading)
            hl.setFont(QFont("Segoe UI", 9, QFont.Weight.Bold))
            hl.setStyleSheet(f"color:{C_TEXT};background:transparent;")
            hl.setWordWrap(True)
            tb.addWidget(hl)
            bl = QLabel(body)
            bl.setFont(QFont("Segoe UI", 9))
            bl.setStyleSheet(f"color:{C_MUTED};background:transparent;")
            bl.setWordWrap(True)
            tb.addWidget(bl)
            sl.addLayout(tb, 1)
            lay.addWidget(sf)

        warn = QLabel("☑  Else you will get 1132 error!")
        warn.setFont(QFont("Segoe UI", 9, QFont.Weight.Bold))
        warn.setStyleSheet(f"color:{C_ORANGE};background:rgba(255,107,0,0.08);border:1px solid rgba(255,107,0,0.25);border-left:3px solid {C_ORANGE};border-radius:4px;padding:10px 14px;")
        warn.setWordWrap(True)
        lay.addWidget(warn)

        gi = NeonButton("Got It", C_CYAN)
        gi.clicked.connect(self.accept)
        lay.addWidget(gi)


# ── SEQUENCE CONFIRM DIALOG ───────────────────────────────────────────────────

class SequenceConfirmDialog(QDialog):
    def __init__(self, old_user, new_user, new_pass, parent=None):
        super().__init__(parent)
        self.setModal(True)
        self.setFixedWidth(420)
        self.setWindowFlags(
            Qt.WindowType.Dialog |
            Qt.WindowType.FramelessWindowHint |
            Qt.WindowType.WindowStaysOnTopHint
        )
        self.setStyleSheet(f"QDialog{{background-color:{C_BG_CARD};border:1px solid rgba(255,215,0,0.4);border-radius:8px;}}")
        lay = QVBoxLayout(self)
        lay.setContentsMargins(28, 24, 28, 24)
        lay.setSpacing(14)

        line = QFrame()
        line.setFixedHeight(1)
        line.setStyleSheet(f"background:qlineargradient(x1:0,y1:0,x2:1,y2:0,stop:0 transparent,stop:0.5 {C_GOLD},stop:1 transparent);")
        lay.addWidget(line)

        title = QLabel("⚡  FULL AUTO-SEQUENCE")
        title.setFont(QFont("Consolas", 12, QFont.Weight.Bold))
        title.setStyleSheet(f"color:{C_GOLD}; letter-spacing:2px;")
        lay.addWidget(title)

        sub = QLabel("The following will happen in order:")
        sub.setFont(QFont("Segoe UI", 9))
        sub.setStyleSheet(f"color:{C_MUTED};")
        lay.addWidget(sub)

        steps_info = [
            (C_ORANGE, "Kill Zoom process (if running)"),
            (C_ORANGE, "Uninstall Zoom via CleanZoom"),
            (C_RED,    f"Wipe previous user:  {old_user}" if old_user else "No previous user to wipe  (skipped)"),
            (C_GREEN,  f"Create:  {new_user}     password: {new_pass}"),
            (C_CYAN,   "Download Zoom MSI → C:\\Users\\Public\\Downloads"),
            (C_GREEN,  f"Install Zoom silently as user '{new_user}'"),
        ]
        for color, text in steps_info:
            r = QHBoxLayout()
            dot = QLabel("▸")
            dot.setFont(QFont("Segoe UI", 10))
            dot.setStyleSheet(f"color:{color};background:transparent;")
            dot.setFixedWidth(18)
            r.addWidget(dot)
            lb = QLabel(text)
            lb.setFont(QFont("Segoe UI", 9))
            lb.setStyleSheet(f"color:{C_TEXT};background:transparent;")
            lb.setWordWrap(True)
            r.addWidget(lb, 1)
            lay.addLayout(r)

        warn = QLabel("⚠  This cannot be undone. Make sure nobody else is using Zoom right now.")
        warn.setFont(QFont("Segoe UI", 8))
        warn.setStyleSheet(f"color:{C_ORANGE};background:rgba(255,107,0,0.08);border:1px solid rgba(255,107,0,0.25);border-left:3px solid {C_ORANGE};border-radius:4px;padding:8px 12px;")
        warn.setWordWrap(True)
        lay.addWidget(warn)

        btn_row = QHBoxLayout()
        btn_row.setSpacing(10)
        go = NeonButton("⚡  LET'S GO", C_GOLD)
        go.clicked.connect(self.accept)
        btn_row.addWidget(go)
        ca = NeonButton("Cancel", C_MUTED)
        ca.clicked.connect(self.reject)
        btn_row.addWidget(ca)
        lay.addLayout(btn_row)


# ── MAIN WINDOW ───────────────────────────────────────────────────────────────

class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("Will's VIP 1132 User Manager")
        self.setMinimumWidth(640)
        self.resize(720, 640)
        self._worker      = None
        self._active_user = STATE.current_username()
        self._busy        = False
        self._blink_state = True

        self._build_ui()
        self._start_blink_timer()
        self._update_active_user(self._active_user)
        self._update_sequence_btn_label()

        self._log("[INIT] Will's VIP 1132 Manager loaded.", "info")
        self._log(f"[INIT] Machine: {socket.gethostname().upper()} · System ready.", "data")
        if self._active_user:
            self._log(f"[INIT] Remembered active Zoom user: {self._active_user}  →  next will be: {STATE.next_username()}", "info")
        self._log("[WAIT] Awaiting command...", "data")

    def _build_ui(self):
        self.bg = BackgroundWidget(self)
        self.setCentralWidget(self.bg)

        wrapper = QWidget(self.bg)
        wrapper.setStyleSheet("background:transparent;")
        ml = QVBoxLayout(wrapper)
        ml.setContentsMargins(24, 16, 24, 16)
        ml.setSpacing(8)

        ml.addWidget(self._build_header())
        ml.addWidget(self._build_status_strip())
        ml.addWidget(self._build_buttons_panel())
        ml.addWidget(self._build_terminal())
        ml.addWidget(self._build_footer())

        outer = QVBoxLayout(self.bg)
        outer.setContentsMargins(0, 0, 0, 0)
        outer.addWidget(wrapper)

    def _build_header(self):
        w = QWidget()
        w.setStyleSheet("background:transparent;")
        lay = QVBoxLayout(w)
        lay.setSpacing(3)
        lay.setContentsMargins(0, 0, 0, 0)
        lay.setAlignment(Qt.AlignmentFlag.AlignHCenter)

        badge = QLabel("VIP SYSTEM v2.1")
        badge.setFont(QFont("Segoe UI", 7, QFont.Weight.Medium))
        badge.setAlignment(Qt.AlignmentFlag.AlignCenter)
        badge.setStyleSheet(f"color:{C_CYAN};border:1px solid rgba(0,245,255,0.2);border-radius:2px;padding:2px 10px;letter-spacing:3px;background:transparent;")
        lay.addWidget(badge, alignment=Qt.AlignmentFlag.AlignHCenter)

        title = QLabel("WILL'S VIP  1132  USER MANAGER")
        title.setFont(QFont("Consolas", 15, QFont.Weight.Bold))
        title.setAlignment(Qt.AlignmentFlag.AlignCenter)
        title.setStyleSheet(f"color:{C_CYAN};letter-spacing:3px;background:transparent;")
        lay.addWidget(title)

        sub = QLabel("ZOOM ERROR BYPASS CONTROL PANEL")
        sub.setFont(QFont("Segoe UI", 7))
        sub.setAlignment(Qt.AlignmentFlag.AlignCenter)
        sub.setStyleSheet(f"color:{C_MUTED};letter-spacing:4px;background:transparent;")
        lay.addWidget(sub)
        return w

    def _build_status_strip(self):
        w = QFrame()
        w.setStyleSheet(f"QFrame{{background:{C_BG_PAN};border:1px solid rgba(0,245,255,0.15);border-left:3px solid {C_GREEN};border-radius:4px;}}")
        lay = QHBoxLayout(w)
        lay.setContentsMargins(12, 7, 12, 7)
        lay.setSpacing(10)

        self._dot = QLabel("●")
        self._dot.setFont(QFont("Arial", 8))
        self._dot.setStyleSheet(f"color:{C_GREEN};background:transparent;")
        lay.addWidget(self._dot)

        for lt, vt in [("MACHINE", socket.gethostname().upper()), ("SYSTEM", "ONLINE")]:
            ll = QLabel(lt)
            ll.setFont(QFont("Segoe UI", 7))
            ll.setStyleSheet(f"color:{C_MUTED};letter-spacing:2px;background:transparent;")
            lay.addWidget(ll)
            vl = QLabel(vt)
            vl.setFont(QFont("Consolas", 11, QFont.Weight.Bold))
            vl.setStyleSheet(f"color:{C_CYAN};background:transparent;")
            lay.addWidget(vl)
            if lt == "MACHINE":
                s = QLabel("·")
                s.setStyleSheet(f"color:{C_DIM};background:transparent;")
                lay.addWidget(s)

        lay.addStretch()

        al = QLabel("ACTIVE ZOOM USER")
        al.setFont(QFont("Segoe UI", 7))
        al.setStyleSheet(f"color:{C_MUTED};letter-spacing:2px;background:transparent;")
        lay.addWidget(al)

        self._active_user_lbl = QLabel("— NONE —")
        self._active_user_lbl.setFont(QFont("Consolas", 10, QFont.Weight.Bold))
        self._active_user_lbl.setStyleSheet(f"color:{C_PINK};background:rgba(255,0,200,0.08);border:1px solid rgba(255,0,200,0.2);border-radius:3px;padding:3px 10px;")
        lay.addWidget(self._active_user_lbl)
        return w

    def _build_buttons_panel(self):
        panel = QFrame()
        panel.setStyleSheet(f"QFrame{{background:{C_BG_PAN};border:1px solid rgba(0,245,255,0.15);border-radius:6px;}}")
        lay = QVBoxLayout(panel)
        lay.setContentsMargins(18, 14, 18, 14)
        lay.setSpacing(7)

        # ── HERO: full auto sequence ──
        self._btn_sequence = NeonButton("⚡  DO IT ALL", C_GOLD)
        self._btn_sequence.setFixedHeight(44)
        self._btn_sequence.setFont(QFont("Segoe UI", 10, QFont.Weight.Bold))
        self._btn_sequence.clicked.connect(self.on_full_sequence)
        lay.addWidget(self._btn_sequence)

        div0 = QFrame()
        div0.setFixedHeight(1)
        div0.setStyleSheet(f"background:rgba(255,215,0,0.12);border:none;")
        lay.addWidget(div0)

        # ── ZOOM OPERATIONS ──
        s2 = QLabel("// ZOOM OPERATIONS")
        s2.setFont(QFont("Consolas", 8))
        s2.setStyleSheet(f"color:{C_DIM};letter-spacing:3px;background:transparent;")
        lay.addWidget(s2)

        r2 = QHBoxLayout()
        r2.setSpacing(10)
        btn_uninst = NeonButton("⊘  UNINSTALL ZOOM", C_ORANGE)
        btn_uninst.clicked.connect(self.on_uninstall_zoom)
        r2.addWidget(btn_uninst)
        btn_dl = NeonButton("↓  DOWNLOAD ZOOM", C_GREEN)
        btn_dl.clicked.connect(self.on_download_zoom)
        r2.addWidget(btn_dl)
        lay.addLayout(r2)

        r3 = QHBoxLayout()
        r3.setSpacing(10)
        btn_open = NeonButton("⊡  OPEN DOWNLOADS", C_CYAN)
        btn_open.clicked.connect(self.on_open_downloads)
        r3.addWidget(btn_open)
        btn_inst = NeonButton("ⓘ  INSTRUCTIONS", C_PURPLE)
        btn_inst.clicked.connect(self.on_instructions)
        r3.addWidget(btn_inst)
        lay.addLayout(r3)

        div1 = QFrame()
        div1.setFixedHeight(1)
        div1.setStyleSheet(f"background:rgba(0,245,255,0.08);border:none;")
        lay.addWidget(div1)

        # ── USER MANAGEMENT ──
        s1 = QLabel("// USER MANAGEMENT")
        s1.setFont(QFont("Consolas", 8))
        s1.setStyleSheet(f"color:{C_DIM};letter-spacing:3px;background:transparent;")
        lay.addWidget(s1)

        r1 = QHBoxLayout()
        r1.setSpacing(10)
        btn_create = NeonButton("◈  CREATE NEW USER", C_CYAN)
        btn_create.clicked.connect(self.on_create_user)
        r1.addWidget(btn_create)
        btn_delete = NeonButton("✕  DELETE USER", C_PINK)
        btn_delete.clicked.connect(self.on_delete_user)
        r1.addWidget(btn_delete)
        lay.addLayout(r1)

        btn_list = NeonButton("≡  LIST ALL USERS", C_PURPLE)
        btn_list.clicked.connect(self.on_list_users)
        lay.addWidget(btn_list)

        self._all_buttons = [
            self._btn_sequence,
            btn_uninst, btn_dl, btn_open, btn_inst,
            btn_create, btn_delete, btn_list,
        ]
        return panel

    def _build_terminal(self):
        panel = QFrame()
        panel.setStyleSheet(f"QFrame{{background:{C_BG_PAN};border:1px solid rgba(0,245,255,0.15);border-radius:6px;}}")
        lay = QVBoxLayout(panel)
        lay.setContentsMargins(0, 16, 0, 0)
        lay.setSpacing(0)

        lbl = QLabel("  > SYSTEM OUTPUT")
        lbl.setFont(QFont("Consolas", 8))
        lbl.setStyleSheet(f"color:{C_MUTED};letter-spacing:2px;background:transparent;padding-left:16px;")
        lay.addWidget(lbl)

        self.terminal = QTextEdit()
        self.terminal.setReadOnly(True)
        self.terminal.setMinimumHeight(110)
        self.terminal.setMaximumHeight(150)
        self.terminal.setFont(QFont("Consolas", 10))
        self.terminal.setStyleSheet(f"""
            QTextEdit{{background:{C_BG_DEEP};color:{C_MUTED};border:none;
                border-top:1px solid rgba(0,245,255,0.08);
                border-radius:0 0 6px 6px;padding:12px 16px;}}
            QScrollBar:vertical{{background:{C_BG_DEEP};width:4px;}}
            QScrollBar::handle:vertical{{background:rgba(0,245,255,0.2);border-radius:2px;}}
        """)
        lay.addWidget(self.terminal)
        return panel

    def _build_footer(self):
        w = QLabel("Built for  WILL's VIP ROOM  ·  Zoom 1132 Fix System")
        w.setFont(QFont("Segoe UI", 8))
        w.setAlignment(Qt.AlignmentFlag.AlignCenter)
        w.setStyleSheet(f"color:{C_DIM};letter-spacing:2px;background:transparent;")
        return w

    # ── BLINK ─────────────────────────────────────────────────────────────────

    def _start_blink_timer(self):
        t = QTimer(self)
        t.timeout.connect(self._blink_tick)
        t.start(900)

    def _blink_tick(self):
        self._blink_state = not self._blink_state
        a = "ff" if self._blink_state else "40"
        self._dot.setStyleSheet(f"color:#{a}ff88;background:transparent;")

    # ── LOG ───────────────────────────────────────────────────────────────────

    def _log(self, message, level="info"):
        cm = {"info":C_CYAN,"success":C_GREEN,"warn":C_ORANGE,"error":C_RED,"data":C_MUTED}
        pm = {"info":"INFO","success":" OK ","warn":"WARN","error":" ERR","data":"DATA"}
        color  = cm.get(level, C_TEXT)
        prefix = pm.get(level, "INFO")
        ts     = datetime.now().strftime("%H:%M:%S")

        cur = self.terminal.textCursor()
        cur.movePosition(QTextCursor.MoveOperation.End)

        fmt_ts = QTextCharFormat()
        fmt_ts.setForeground(QColor(C_DIM))
        cur.insertText(f"[{ts}] ", fmt_ts)

        fmt_c = QTextCharFormat()
        fmt_c.setForeground(QColor(color))
        cur.insertText(f"[{prefix}] {message}\n", fmt_c)

        self.terminal.setTextCursor(cur)
        self.terminal.ensureCursorVisible()

    # ── BUSY ──────────────────────────────────────────────────────────────────

    def _set_busy(self, busy):
        self._busy = busy
        for btn in getattr(self, "_all_buttons", []):
            btn.setEnabled(not busy)

    # ── USER DISPLAY ──────────────────────────────────────────────────────────

    def _update_active_user(self, username=None):
        self._active_user = username
        self._active_user_lbl.setText(username.upper() if username else "— NONE —")

    def _update_sequence_btn_label(self):
        nxt     = STATE.next_username()
        nxt_num = STATE.next_user_num()
        old     = STATE.current_username()
        old_str = f"  |  wipes: {old}" if old else "  |  no previous user"
        self._btn_sequence.setText(
            f"⚡  DO IT ALL  —  creates: {nxt}  (pw: {nxt_num}){old_str}"
        )

    # ── WORKER ────────────────────────────────────────────────────────────────

    def _start_worker(self, task, **kwargs):
        if self._busy:
            return
        self._set_busy(True)
        self._worker = Worker(task, **kwargs)
        self._worker.log_signal.connect(self._log)
        self._worker.done_signal.connect(self._on_done)
        self._worker.error_signal.connect(self._on_error)
        self._worker.start()

    def _on_done(self, result):
        self._set_busy(False)

        if result.startswith("create_ok:"):
            uname = result.split(":", 1)[1]
            STATE.advance()
            self._update_active_user(uname)
            self._update_sequence_btn_label()

        elif result.startswith("delete_ok:"):
            uname = result.split(":", 1)[1]
            if self._active_user and self._active_user.lower() == uname.lower():
                self._update_active_user(None)

        elif result == "uninstall_ok":
            self._update_active_user(None)

        elif result.startswith("list_ok:"):
            users = [u for u in result.split(":", 1)[1].split(",") if u]
            UsersDialog(users, self._active_user, self).exec()
            self._log(f"Listed {len(users)} accounts on \\\\{socket.gethostname().upper()}", "info")

        elif result.startswith("sequence_ok:"):
            uname = result.split(":", 1)[1]
            STATE.advance()
            self._update_active_user(uname)
            self._update_sequence_btn_label()
            self._log(f"Active user: {uname.upper()}  |  Next sequence → {STATE.next_username()}", "info")

        elif result == "sequence_fail":
            self._log("Sequence aborted — check errors above.", "error")

    def _on_error(self, err):
        self._set_busy(False)
        self._log(f"EXCEPTION: {err}", "error")

    # ── HANDLERS ──────────────────────────────────────────────────────────────

    def on_full_sequence(self):
        old_user = STATE.current_username()
        new_num  = STATE.next_user_num()
        new_user = str(new_num)
        new_pass = str(new_num)

        dlg = SequenceConfirmDialog(old_user, new_user, new_pass, self)
        if dlg.exec() != QDialog.DialogCode.Accepted:
            return
        self._start_worker("full_sequence",
                           old_username=old_user,
                           new_username=new_user,
                           new_password=new_pass)

    def on_create_user(self):
        dlg = NeonDialog("Create New User", "New Username", confirm_text="Create", accent=C_CYAN, parent=self)
        if dlg.exec() == QDialog.DialogCode.Accepted:
            name = dlg.get_value()
            if not name:
                self._log("ERROR: Username cannot be empty.", "error")
                return
            self._start_worker("create_user", username=name, password=name)

    def on_delete_user(self):
        dlg = NeonDialog("Delete User", "Username to Delete", confirm_text="Delete", accent=C_PINK, parent=self)
        if dlg.exec() == QDialog.DialogCode.Accepted:
            name = dlg.get_value()
            if not name:
                self._log("ERROR: Username cannot be empty.", "error")
                return
            self._start_worker("delete_user", username=name)

    def on_list_users(self):
        self._start_worker("list_users")

    def on_uninstall_zoom(self):
        self._start_worker("uninstall_zoom")

    def on_download_zoom(self):
        self._start_worker("download_zoom")

    def on_open_downloads(self):
        self._start_worker("open_downloads")

    def on_instructions(self):
        InstructionsDialog(self).exec()

    def resizeEvent(self, event):
        self.bg.resize(self.centralWidget().size())
        super().resizeEvent(event)


# ── ENTRY POINT ───────────────────────────────────────────────────────────────

if __name__ == "__main__":
    # ── Auto-elevate to Administrator if not already ──────────────────────────
    import ctypes
    def is_admin():
        try:
            return ctypes.windll.shell32.IsUserAnAdmin()
        except Exception:
            return False

    if not is_admin():
        ctypes.windll.shell32.ShellExecuteW(
            None, "runas",
            sys.executable,
            " ".join(f'"{a}"' for a in sys.argv),
            None, 1
        )
        sys.exit(0)

    app = QApplication(sys.argv)
    app.setStyle("Fusion")

    pal = QPalette()
    pal.setColor(QPalette.ColorRole.Window,          QColor(C_BG_DEEP))
    pal.setColor(QPalette.ColorRole.WindowText,      QColor(C_TEXT))
    pal.setColor(QPalette.ColorRole.Base,            QColor(C_BG_PAN))
    pal.setColor(QPalette.ColorRole.AlternateBase,   QColor(C_BG_CARD))
    pal.setColor(QPalette.ColorRole.Text,            QColor(C_TEXT))
    pal.setColor(QPalette.ColorRole.Button,          QColor(C_BG_CARD))
    pal.setColor(QPalette.ColorRole.ButtonText,      QColor(C_TEXT))
    pal.setColor(QPalette.ColorRole.Highlight,       QColor(C_CYAN))
    pal.setColor(QPalette.ColorRole.HighlightedText, QColor(C_BG_DEEP))
    app.setPalette(pal)

    win = MainWindow()
    win.show()
    sys.exit(app.exec())
