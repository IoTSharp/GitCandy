const searchInput = document.querySelector("[data-help-search]");
const searchStatus = document.querySelector("[data-search-status]");
const searchResults = document.querySelector("[data-search-results]");
const menuButton = document.querySelector("[data-help-menu]");
const sidebar = document.querySelector("[data-help-sidebar]");

if (menuButton && sidebar) {
  menuButton.addEventListener("click", () => {
    const isOpen = sidebar.toggleAttribute("data-open");
    menuButton.setAttribute("aria-expanded", String(isOpen));
  });
}

if (searchInput && searchStatus && searchResults) {
  let documents;

  searchInput.addEventListener("input", async () => {
    const query = searchInput.value.trim().toLocaleLowerCase("zh-CN");
    searchResults.replaceChildren();
    searchStatus.textContent = "";
    if (query.length < 2) return;

    try {
      documents ??= await fetch(searchInput.dataset.searchIndex, { credentials: "same-origin" })
        .then((response) => {
          if (!response.ok) throw new Error("search index unavailable");
          return response.json();
        });
    } catch {
      searchStatus.textContent = "搜索索引暂不可用。";
      return;
    }

    const matches = documents
      .filter((document) => `${document.title} ${document.summary} ${document.keywords}`.toLocaleLowerCase("zh-CN").includes(query))
      .slice(0, 8);
    searchStatus.textContent = matches.length ? `找到 ${matches.length} 篇文档` : "没有匹配的当前文档。";
    for (const match of matches) {
      const item = document.createElement("li");
      const link = document.createElement("a");
      const indexUrl = new URL(searchInput.dataset.searchIndex, document.baseURI);
      link.href = new URL(match.url, indexUrl).href;
      link.textContent = match.title;
      item.append(link);
      searchResults.append(item);
    }
  });
}
