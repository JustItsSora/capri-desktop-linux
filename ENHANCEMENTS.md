# Capri Linux Enhanced — what this fork adds

A community fork of [Capri by Lachy](https://github.com/lachydotmcg/capri-desktop).
The upstream project is the star of the show; this fork exists because Capri's
**Linux and Mac** builds were an early preview where most of what makes her
Capri — her hands, her eyes, her brain — was stubbed out.

Nothing here changes her personality or her voice. This is all plumbing.

---

## 1. She sees and controls your windows

Upstream, `Hands.Windows` returned an empty list on Linux and Mac, so she could
not tell what you had open and every window action failed.

This fork implements the X11 side of her hands with `libX11` (no extra packages
needed, no `xdotool` required for the window work):

| What she can do | How |
| --- | --- |
| List open windows with their title and app | `_NET_CLIENT_LIST`, `_NET_WM_NAME`, `WM_CLASS`, `_NET_WM_PID` |
| Bring a window to the front | `_NET_ACTIVE_WINDOW` + `XRaiseWindow` |
| Minimize a window | `_NET_WM_STATE` / `_NET_WM_STATE_HIDDEN` |
| Close a window | `_NET_CLOSE_WINDOW` (a polite close, so apps can ask to save) |
| Close a browser tab | `xdotool key ctrl+w` |

She skips her own windows by PID, and windows on other desktops are filtered
through `_NET_WM_DESKTOP`, so she only ever acts on what you can actually see.

Her Windows behaviour is untouched: the Windows build keeps using its own
`user32.dll` implementation.

## 2. She can look at your screen

Upstream, the `look_at_screen` tool was Windows-only. On Linux she now takes a
real screenshot of the root window with ImageMagick's `import`, sends it as a
JPEG and asks a **vision-capable** model what is on it.

You do not have to pick a vision model yourself: when her brain is a text-only
one, she borrows `meta/llama-3.2-11b-vision-instruct` for the look and gives
it back afterwards. Any multimodal Ollama model works too.

## 3. She has hands: mouse and keyboard on Linux

The `use_computer` tool was a stub outside Windows. On Linux she now really
drives the desktop, in two steps:

1. her text brain plans **one small step at a time** — `LOOK`, `CLICK`, `TYPE`,
   `KEY`, `SCROLL`, `WAIT`, `SAY` or `DONE`;
2. her eyes find the pixels, and `xdotool` does the moving and the typing.

Clicks first look for a window with that name (`xdotool search --name`), which
is exact; only when there is no such window does she ask a vision model where
the thing is, and it answers with a position from 0 to 1 that she turns into
pixels.

She stops when she is stuck, when the same step repeats, or after a dozen
steps, and she tells you what she got through.

You can also give her a job from a terminal, which is handy while you are
using the mouse yourself:

```bash
out/linux/Capri --hands "tell me what app is open on the screen"
```

## 4. Shortcuts that work on your desktop

Upstream, every shortcut but the Discord ones said "that only works on
Windows". On Linux she now does the ones your desktop actually has:

| Shortcut | What she does |
| --- | --- |
| screenshot | a full-screen shot with `xfce4-screenshooter`, saved in your Pictures folder |
| show desktop | `super+d` |
| task manager | `xfce4-appfinder`, or `htop` in a terminal |
| clipboard | reads your clipboard and shows it |
| record | records the screen with `ffmpeg` |
| clip that | saves the last 30 seconds of that recording to your Videos folder |
| discord mute / deafen | the same Discord shortcuts as on Windows |
| game bar / emoji | she says plainly that your desktop has neither |

## 5. Clipboard and reminders

Two more tools, only on Linux and Mac, so her brain does not have to guess:

- **clipboard** — read what you copied, or put text there for you to paste
- **remind** — a notification now, or a notification later, with
  `notify-send`

## 6. Power on Linux

The `power` tool was Windows-only too. On Linux she can shut down, restart,
sleep, lock, sign out, or cancel a shutdown she set up, using `systemctl`,
`loginctl` and `xfce4-session-logout`. A countdown works as it does on
Windows.

## 7. She can use a cloud brain (NVIDIA, OpenAI, Claude, DeepSeek)

Upstream, the whole cloud-brain setup lived behind the Windows-only Mainframe,
which left Linux and Mac users with Ollama as the only option — including a
multi-gigabyte local download on a machine with no GPU.

This fork adds a **brain switcher to her right-click menu on Linux and Mac**:

- paste an API key (NVIDIA keys start with `nvapi-`)
- the key is stored in `~/.config/Capri/cloud-key` with `0600` permissions and
  is only ever sent to the provider it belongs to
- the menu lists **the models your key can actually use**, fetched from the
  provider's own API
- pick one and she switches to it, keeping your choice across restarts
- one click to go back to Ollama

A 4B NVIDIA model answers in under a second, where a 120B one takes longer than
her patience, so the default here is the fast one and the big ones are one
click away.

## 8. A single-file Linux build that fits in your pocket

Upstream ships a ~103 MB Linux binary. This fork publishes a **56 MB** single
file with the .NET runtime and Avalonia's native libraries embedded:

```bash
dotnet publish cross/Capri.Cross.csproj -c Release -r linux-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o out/linux
```

The GC heap is also capped at 2 GB so she stays a good neighbour on small
machines (`cross/runtimeconfig.template.json`).

## 9. Tests

The upstream smoke test suite covers the cross-platform build. This fork adds
three cases to it:

- **windows** — she sees the open windows with their titles, switches to,
  minimizes and restores them, and takes screenshots
- **NVIDIA** — her NVIDIA menu lists the models a key can use, picks one for
  the brain, and goes back to Ollama when she disconnects
- **tools** — she reads and writes the clipboard, sets reminders, answers the
  shortcuts the desktop actually has, says what it cannot do, and cancels a
  shutdown that was never going to happen

Run them with:

```bash
out/linux/Capri --smoke "$PWD/smoke/linux"
```

---

## Credits

- **Capri** — created by [Lachy](https://github.com/lachydotmcg).
  Everything about her, including the code this fork is based on, is his work.
- This fork keeps the upstream [PolyForm Noncommercial 1.0.0](LICENSE.md)
  license and the author's Required Notice exactly as it was.
- The Linux additions in this fork were written by the maintainers of
  [capri-desktop-linux](https://github.com/kyuog/capri-desktop-linux).

## License

Unchanged from upstream: **PolyForm Noncommercial 1.0.0**. Noncommercial use
only. See [LICENSE.md](LICENSE.md).
