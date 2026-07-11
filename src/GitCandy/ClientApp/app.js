import {
  Archive,
  ArrowLeft,
  Check,
  ChevronDown,
  CircleAlert,
  Clipboard,
  Code2,
  Copy,
  ExternalLink,
  FileKey,
  FolderGit2,
  Gauge,
  GitBranch,
  KeyRound,
  Languages,
  Lightbulb,
  LockKeyhole,
  LogIn,
  LogOut,
  Menu,
  Monitor,
  Moon,
  Plus,
  Search,
  Settings,
  Shield,
  Sun,
  Trash2,
  User,
  UserPlus,
  UserRoundCog,
  Users,
  X,
  createIcons
} from "lucide";

const themeCookieName = ".GitCandy.Theme";
const supportedThemes = new Set(["system", "light", "dark"]);

function applyTheme(theme) {
  if (!supportedThemes.has(theme)) {
    return;
  }

  document.documentElement.dataset.theme = theme;
  document.cookie = `${themeCookieName}=${theme}; Path=/; Max-Age=31536000; SameSite=Lax`;
  document.querySelectorAll("[data-theme-value]").forEach((button) => {
    const selected = button.dataset.themeValue === theme;
    button.setAttribute("aria-pressed", selected.toString());
  });
}

function initializeThemeControl() {
  document.querySelectorAll("[data-theme-value]").forEach((button) => {
    button.addEventListener("click", () => applyTheme(button.dataset.themeValue));
  });
}

function initializeNavigation() {
  const toggle = document.querySelector("[data-navigation-toggle]");
  const drawer = document.querySelector("[data-navigation-drawer]");
  const backdrop = document.querySelector("[data-navigation-backdrop]");
  if (!(toggle instanceof HTMLElement) || !(drawer instanceof HTMLElement)) {
    return;
  }

  const mobileViewport = window.matchMedia("(max-width: 860px)");
  let lastFocused = null;
  const focusableSelector = "a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), summary, [tabindex]:not([tabindex='-1'])";
  const synchronizeDrawer = () => {
    const isOpen = document.body.classList.contains("navigation-open");
    drawer.inert = mobileViewport.matches && !isOpen;
    drawer.setAttribute("aria-hidden", (mobileViewport.matches && !isOpen).toString());
  };
  const close = () => {
    document.body.classList.remove("navigation-open");
    toggle.setAttribute("aria-expanded", "false");
    synchronizeDrawer();
    if (lastFocused instanceof HTMLElement) {
      lastFocused.focus();
    }
  };
  const open = () => {
    lastFocused = document.activeElement;
    document.body.classList.add("navigation-open");
    toggle.setAttribute("aria-expanded", "true");
    drawer.setAttribute("aria-hidden", "false");
    drawer.inert = false;
    drawer.querySelector("a, button")?.focus();
  };

  toggle.addEventListener("click", () => {
    document.body.classList.contains("navigation-open") ? close() : open();
  });
  backdrop?.addEventListener("click", close);
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && document.body.classList.contains("navigation-open")) {
      close();
    }

    if (event.key === "Tab" && document.body.classList.contains("navigation-open")) {
      const focusable = [...drawer.querySelectorAll(focusableSelector)].filter((element) => !element.inert);
      const first = focusable.at(0);
      const last = focusable.at(-1);
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last?.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first?.focus();
      }
    }
  });
  mobileViewport.addEventListener("change", () => {
    if (!mobileViewport.matches) {
      document.body.classList.remove("navigation-open");
      toggle.setAttribute("aria-expanded", "false");
    }
    synchronizeDrawer();
  });
  synchronizeDrawer();
}

function initializeCopyButtons() {
  document.querySelectorAll("[data-copy-target]").forEach((button) => {
    button.addEventListener("click", async () => {
      const target = document.querySelector(button.dataset.copyTarget);
      if (!(target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement)) {
        return;
      }

      try {
        await navigator.clipboard.writeText(target.value);
        button.dataset.tooltip = button.dataset.copied ?? "Copied";
        window.setTimeout(() => {
          button.dataset.tooltip = button.dataset.label ?? "Copy";
        }, 1600);
      } catch {
        target.select();
        document.execCommand("copy");
      }
    });
  });
}

function initializeConfirmations() {
  document.querySelectorAll("[data-confirm]").forEach((form) => {
    form.addEventListener("submit", (event) => {
      if (!window.confirm(form.dataset.confirm)) {
        event.preventDefault();
      }
    });
  });
}

createIcons({
  icons: {
    Archive,
    ArrowLeft,
    Check,
    ChevronDown,
    CircleAlert,
    Clipboard,
    Code2,
    Copy,
    ExternalLink,
    FileKey,
    FolderGit2,
    Gauge,
    GitBranch,
    KeyRound,
    Languages,
    Lightbulb,
    LockKeyhole,
    LogIn,
    LogOut,
    Menu,
    Monitor,
    Moon,
    Plus,
    Search,
    Settings,
    Shield,
    Sun,
    Trash2,
    User,
    UserPlus,
    UserRoundCog,
    Users,
    X
  },
  attrs: { "aria-hidden": "true", "stroke-width": 1.8 }
});
initializeThemeControl();
initializeNavigation();
initializeCopyButtons();
initializeConfirmations();
