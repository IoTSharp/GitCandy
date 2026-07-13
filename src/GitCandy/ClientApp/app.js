import {
  Activity,
  Archive,
  ArrowLeft,
  ArrowRight,
  Box,
  Bell,
  Check,
  CheckCheck,
  CheckSquare,
  ChevronDown,
  CircleAlert,
  CircleCheck,
  CircleCheckBig,
  CircleDot,
  Clock3,
  Clipboard,
  Code2,
  Copy,
  Compass,
  Download,
  ExternalLink,
  EyeOff,
  FileCode2,
  FileKey,
  FolderGit2,
  Folder,
  FolderTree,
  Gauge,
  GitBranch,
  GitCompareArrows,
  GitCommitHorizontal,
  History,
  Inbox,
  KeyRound,
  Languages,
  Lightbulb,
  LayoutDashboard,
  ListFilter,
  Link,
  Lock,
  LockKeyhole,
  LogIn,
  LogOut,
  Menu,
  MessageSquare,
  Monitor,
  Moon,
  Plus,
  PackageOpen,
  Pause,
  Pencil,
  Play,
  RotateCcw,
  RotateCw,
  Radio,
  Search,
  SearchX,
  ScanText,
  Settings,
  Settings2,
  Shield,
  ShieldCheck,
  Star,
  Square,
  Sun,
  Trash2,
  User,
  UserPlus,
  UserRound,
  UserRoundCog,
  Users,
  Webhook,
  X,
  createIcons
} from "lucide";
import hljs from "highlight.js/lib/core";
import bash from "highlight.js/lib/languages/bash";
import csharp from "highlight.js/lib/languages/csharp";
import css from "highlight.js/lib/languages/css";
import diff from "highlight.js/lib/languages/diff";
import go from "highlight.js/lib/languages/go";
import javascript from "highlight.js/lib/languages/javascript";
import json from "highlight.js/lib/languages/json";
import markdown from "highlight.js/lib/languages/markdown";
import plaintext from "highlight.js/lib/languages/plaintext";
import powershell from "highlight.js/lib/languages/powershell";
import python from "highlight.js/lib/languages/python";
import rust from "highlight.js/lib/languages/rust";
import sql from "highlight.js/lib/languages/sql";
import typescript from "highlight.js/lib/languages/typescript";
import xml from "highlight.js/lib/languages/xml";
import yaml from "highlight.js/lib/languages/yaml";

hljs.registerLanguage("bash", bash);
hljs.registerLanguage("csharp", csharp);
hljs.registerLanguage("css", css);
hljs.registerLanguage("diff", diff);
hljs.registerLanguage("go", go);
hljs.registerLanguage("javascript", javascript);
hljs.registerLanguage("json", json);
hljs.registerLanguage("markdown", markdown);
hljs.registerLanguage("plaintext", plaintext);
hljs.registerLanguage("powershell", powershell);
hljs.registerLanguage("python", python);
hljs.registerLanguage("razor", xml);
hljs.registerLanguage("rust", rust);
hljs.registerLanguage("sql", sql);
hljs.registerLanguage("typescript", typescript);
hljs.registerLanguage("xml", xml);
hljs.registerLanguage("yaml", yaml);

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

function initializeLandingPage() {
  const body = document.querySelector(".landing-body");
  if (!(body instanceof HTMLElement)) {
    return;
  }

  const toggle = document.querySelector("[data-landing-navigation-toggle]");
  const navigation = document.querySelector("[data-landing-navigation]");
  const closeNavigation = () => {
    navigation?.classList.remove("is-open");
    toggle?.setAttribute("aria-expanded", "false");
  };

  toggle?.addEventListener("click", () => {
    const isOpen = navigation?.classList.toggle("is-open") ?? false;
    toggle.setAttribute("aria-expanded", isOpen.toString());
  });
  navigation?.querySelectorAll("a").forEach((link) => link.addEventListener("click", closeNavigation));
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape") {
      closeNavigation();
    }
  });

  if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
    return;
  }

  document.documentElement.classList.add("landing-motion");
  const observer = new IntersectionObserver((entries) => {
    entries.forEach((entry) => {
      if (entry.isIntersecting) {
        entry.target.classList.add("is-visible");
        observer.unobserve(entry.target);
      }
    });
  }, { rootMargin: "0px 0px -12%", threshold: 0.08 });
  document.querySelectorAll("[data-reveal]").forEach((element) => observer.observe(element));
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

function initializeCodeViews() {
  document.querySelectorAll("[data-highlight]").forEach((element) => hljs.highlightElement(element));

  document.querySelectorAll("[data-code-view]").forEach((view) => {
    const rows = [...view.querySelectorAll("tr[data-line]")];
    let anchorLine = null;
    const selectRange = (start, end) => {
      const lower = Math.min(start, end);
      const upper = Math.max(start, end);
      rows.forEach((row) => row.classList.toggle("is-selected", Number(row.dataset.line) >= lower && Number(row.dataset.line) <= upper));
      history.replaceState(null, "", `${location.pathname}${location.search}#L${lower}${lower === upper ? "" : `-L${upper}`}`);
    };

    view.querySelectorAll("[data-line-anchor]").forEach((link) => {
      link.addEventListener("click", (event) => {
        event.preventDefault();
        const line = Number(link.dataset.lineAnchor);
        if (event.shiftKey && anchorLine !== null) {
          selectRange(anchorLine, line);
        } else {
          anchorLine = line;
          selectRange(line, line);
        }
      });
    });

    const match = location.hash.match(/^#L(\d+)(?:-L(\d+))?$/);
    if (match) {
      anchorLine = Number(match[1]);
      selectRange(anchorLine, Number(match[2] ?? match[1]));
      view.querySelector(`#L${anchorLine}`)?.scrollIntoView({ block: "center" });
    }
  });

  document.querySelectorAll("[data-copy-lines]").forEach((button) => {
    button.addEventListener("click", async () => {
      const selected = [...document.querySelectorAll("[data-code-view] tr.is-selected .line-code")];
      const source = selected.length > 0 ? selected : [...document.querySelectorAll("[data-code-view] .line-code")];
      await navigator.clipboard.writeText(source.map((line) => line.textContent ?? "").join("\n"));
    });
  });
}

createIcons({
  icons: {
    Activity,
    Archive,
    ArrowLeft,
    ArrowRight,
    Box,
    Bell,
    Check,
    CheckCheck,
    CheckSquare,
    ChevronDown,
    CircleAlert,
    CircleCheck,
    CircleCheckBig,
    CircleDot,
    Clock3,
    Clipboard,
    Code2,
    Copy,
    Compass,
    Download,
    ExternalLink,
    EyeOff,
    FileCode2,
    FileKey,
    FolderGit2,
    Folder,
    FolderTree,
    Gauge,
    GitBranch,
    GitCompareArrows,
    GitCommitHorizontal,
    History,
    Inbox,
    KeyRound,
    Languages,
    Lightbulb,
    LayoutDashboard,
    ListFilter,
    Link,
    Lock,
    LockKeyhole,
    LogIn,
    LogOut,
    Menu,
    MessageSquare,
    Monitor,
    Moon,
    Plus,
    PackageOpen,
    Pause,
    Pencil,
    Play,
    RotateCcw,
    RotateCw,
    Radio,
    Search,
    SearchX,
    ScanText,
    Settings,
    Settings2,
    Shield,
    ShieldCheck,
    Star,
    Square,
    Sun,
    Trash2,
    User,
    UserPlus,
    UserRound,
    UserRoundCog,
    Users,
    Webhook,
    X
  },
  attrs: { "aria-hidden": "true", "stroke-width": 1.8 }
});
initializeThemeControl();
initializeNavigation();
initializeLandingPage();
initializeCopyButtons();
initializeConfirmations();
initializeCodeViews();
