# Koncus Nai UX & Visual Observations Backlog

This document tracks user-identified UX and visual polish items discovered during interactive manual verification sessions for resolution during appropriate feature work or post-hardening refinement.

---

## Logged Items

### 1. DEFECT-READER-001: Reading Studio Sidebar Header Scroll Clipping
- **Area:** Reading Studio (`ReaderSidebarView.xaml` & `ReaderWindow.xaml`)
- **Severity:** Visual / Layout Glitch
- **Observation:** When the left sidebar has enough content to scroll vertically (or in smaller window sizes), scrolling down moves the sidebar items upward directly behind the floating hamburger toggle button and "Reading Studio" title text because the header lacks an opaque background / dedicated fixed grid row and the `ScrollViewer` spans the full height from `y=0`.
- **Remediation Plan:** Structure `ReaderSidebarView` with a fixed top header row (outside the `ScrollViewer`) matching the layout architecture of `WorkbenchSidebarView`, ensuring scrolling items neatly disappear under a pinned header boundary.

### 2. FEAT-READER-001: Reading Studio Document Theme Harmonization with App Theme
- **Area:** Reading Studio (`ReadingTextLayout.cs` & `ReaderSidebarView.xaml.cs`)
- **Severity:** UX Consistency / Aesthetic
- **Observation:** When the application theme is toggled from Dark to Light mode, the window shell changes to the Light palette, but the reading document surface statically remains on "Midnight" (pitch black).
- **Remediation Plan:** When the application theme is in Light mode, default the initial reading document theme to "Warm Paper" (`#F9F2E8` paper with `#182237` ink), and default to "Midnight" in Dark mode, while preserving explicit user overrides.

### 3. NOTE-A11Y-001: Windows 11 Contrast Theme Activation
- **Area:** Windows Accessibility Integration
- **Note:** In Windows 11 Settings (`Accessibility > Contrast themes`), selecting a contrast theme (e.g. Dusk, Night sky) from the dropdown requires clicking the **"Apply"** button (or pressing `Left Alt + Left Shift + PrintScreen`) before Windows activates the high-contrast color scheme across the OS.
