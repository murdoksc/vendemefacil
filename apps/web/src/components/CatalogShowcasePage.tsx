import { useState, useEffect, useMemo } from "react";
import { ShoppingCart, Search, LockKeyhole, Send, HelpCircle, PackageOpen, Minus, Plus, Trash2, X } from "lucide-react";

type BusinessInfo = {
  name: string;
  slug: string;
  primaryColor: string;
  accentColor: string;
  buttonColor: string;
  hoverColor: string;
  backgroundColor: string;
  surfaceColor: string;
  textColor: string;
  cornerRadius: number;
  phone: string | null;
  address: string | null;
  logoUrl: string | null;
  planCode: string;
};

type Category = {
  id: string;
  name: string;
};

type CatalogProduct = {
  productId: string;
  productName: string;
  categoryName: string | null;
  categoryId: string | null;
  imageUrl: string | null;
  variantId: string;
  variantName: string;
  sku: string;
  barcode: string | null;
  price: number;
  stock: number;
};

type PublicCatalogPayload = {
  business: BusinessInfo;
  categories: Category[];
  products: CatalogProduct[];
};

type CartItem = {
  product: CatalogProduct;
  quantity: number;
};

const money = new Intl.NumberFormat("es-MX", { style: "currency", currency: "MXN" });

export function CatalogShowcasePage({ slug }: { slug: string }) {
  const [data, setData] = useState<PublicCatalogPayload | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  
  // UX State
  const [search, setSearch] = useState("");
  const [selectedCategoryId, setSelectedCategoryId] = useState<string | null>(null);
  const [cart, setCart] = useState<CartItem[]>([]);
  const [isCartOpen, setIsCartOpen] = useState(false);
  const [brandLogoFailed, setBrandLogoFailed] = useState(false);

  // Fetch Public Catalog on Mount
  useEffect(() => {
    setLoading(true);
    fetch(`/api/v1/public/catalog/${slug}`)
      .then(async (res) => {
        if (!res.ok) {
          throw new Error(res.status === 404 ? "El catálogo de esta tienda no se encuentra disponible." : "Ocurrió un error al cargar el catálogo.");
        }
        return res.json();
      })
      .then((json: PublicCatalogPayload) => {
        setData(json);
        // Apply custom business branding colors to the document variables dynamically!
        if (json.business) {
          const b = json.business;
          const root = document.documentElement.style;
          root.setProperty("--brand-primary", b.primaryColor);
          root.setProperty("--brand-accent", b.accentColor);
          root.setProperty("--brand-button", b.buttonColor);
          root.setProperty("--brand-hover", b.hoverColor);
          root.setProperty("--app-background", b.backgroundColor);
          root.setProperty("--app-surface", b.surfaceColor);
          root.setProperty("--app-text", b.textColor);
          root.setProperty("--app-radius", `${b.cornerRadius}px`);
        }
      })
      .catch((err) => {
        setError(err instanceof Error ? err.message : "No pudimos cargar el catálogo.");
      })
      .finally(() => {
        setLoading(false);
      });
  }, [slug]);

  // Filtered Products
  const filteredProducts = useMemo(() => {
    if (!data) return [];
    return data.products.filter((p) => {
      const matchesSearch = `${p.productName} ${p.variantName} ${p.sku} ${p.categoryName ?? ""}`
        .toLowerCase()
        .normalize("NFD")
        .replace(/[\u0300-\u036f]/g, "")
        .includes(search.toLowerCase().normalize("NFD").replace(/[\u0300-\u036f]/g, ""));
      const matchesCategory = selectedCategoryId === null || p.categoryId === selectedCategoryId;
      return matchesSearch && matchesCategory;
    });
  }, [data, search, selectedCategoryId]);

  // Cart operations
  const addToCart = (product: CatalogProduct) => {
    setCart((prev) => {
      const existing = prev.find((item) => item.product.variantId === product.variantId);
      if (existing) {
        // Enforce stock limit if stock is > 0
        if (product.stock > 0 && existing.quantity >= product.stock) {
          return prev;
        }
        return prev.map((item) =>
          item.product.variantId === product.variantId
            ? { ...item, quantity: item.quantity + 1 }
            : item
        );
      }
      return [...prev, { product, quantity: 1 }];
    });
  };

  const updateQuantity = (variantId: string, delta: number) => {
    setCart((prev) => {
      return prev
        .map((item) => {
          if (item.product.variantId === variantId) {
            const nextQty = item.quantity + delta;
            if (item.product.stock > 0 && nextQty > item.product.stock) return item;
            return { ...item, quantity: nextQty };
          }
          return item;
        })
        .filter((item) => item.quantity > 0);
    });
  };

  const removeFromCart = (variantId: string) => {
    setCart((prev) => prev.filter((item) => item.product.variantId !== variantId));
  };

  const totalAmount = useMemo(() => {
    return cart.reduce((sum, item) => sum + item.product.price * item.quantity, 0);
  }, [cart]);

  const totalItemsCount = useMemo(() => {
    return cart.reduce((sum, item) => sum + item.quantity, 0);
  }, [cart]);

  // Send Order via WhatsApp
  const sendOrderToWhatsApp = () => {
    if (!data?.business.phone) {
      window.alert("Este negocio no tiene configurado un número de contacto.");
      return;
    }

    let phone = data.business.phone.replace(/\D/g, "");
    if (phone.length === 10) phone = `52${phone}`;

    // Format WhatsApp message
    let message = `¡Hola *${data.business.name}*! Me interesa realizar un pedido desde tu catálogo digital:\n\n🛍️ *Detalle de mi pedido:*\n`;
    message += `─────────────────────────\n`;
    cart.forEach((item) => {
      const variantDesc = item.product.variantName !== "Única" ? ` (${item.product.variantName})` : "";
      message += `• *${item.quantity}x* ${item.product.productName}${variantDesc}\n`;
      message += `   SKU: \`${item.product.sku}\` — ${money.format(item.product.price * item.quantity)}\n`;
    });
    message += `─────────────────────────\n`;
    message += `💰 *Total del pedido: ${money.format(totalAmount)}*\n\n`;
    message += `¿Tienen disponibilidad de estos artículos para coordinar el pago y la entrega? ¡Muchas gracias!`;

    window.open(`https://wa.me/${phone}?text=${encodeURIComponent(message)}`, "_blank", "noopener,noreferrer");
  };

  if (loading) {
    return (
      <div style={{ minHeight: "100vh", display: "grid", placeItems: "center", background: "#f5f6f8" }}>
        <div style={{ textAlign: "center" }}>
          <div style={{ width: "50px", height: "50px", border: "4px solid #d6e0da", borderTopColor: "var(--brand-primary, #123f35)", borderRadius: "50%", animation: "spin 1s linear infinite" }} />
          <style>{`@keyframes spin { 0% { transform: rotate(0deg); } 100% { transform: rotate(360deg); } }`}</style>
          <p style={{ marginTop: "16px", fontWeight: 700, color: "#6d7e77" }}>Cargando catálogo...</p>
        </div>
      </div>
    );
  }

  if (error || !data) {
    return (
      <div style={{ minHeight: "100vh", display: "flex", alignItems: "center", justifyContent: "center", background: "#f5f6f8", padding: "20px" }}>
        <div className="card" style={{ maxWidth: "480px", textAlign: "center", padding: "40px 30px" }}>
          <span style={{ display: "inline-flex", padding: "16px", borderRadius: "50%", background: "#fff0ee", color: "#a53c32", marginBottom: "20px" }}>
            <PackageOpen size={36} />
          </span>
          <h2 style={{ fontSize: "1.4em", marginBottom: "10px", fontWeight: 800 }}>Tienda No Encontrada</h2>
          <p style={{ color: "var(--text-muted, #6d7e77)", lineHeight: "1.5", marginBottom: "24px" }}>{error || "El negocio especificado no existe o no se encuentra activo."}</p>
          <a href="/" className="button primary" style={{ display: "inline-flex", textDecoration: "none" }}>Ir a la página de inicio</a>
        </div>
      </div>
    );
  }

  const { business, categories } = data;
  const isEsencialOrNegocio = business.planCode !== "pro";

  return (
    <div style={{ minHeight: "100vh", background: "var(--app-background, #f5f6f8)", color: "var(--app-text, #17362e)", fontFamily: "Manrope, sans-serif", paddingBottom: "80px" }}>
      {/* Brand Header Header */}
      <header style={{ background: "var(--app-surface, #fff)", borderBottom: "1px solid var(--border, #d6e0da)", padding: "24px 20px" }}>
        <div style={{ maxWidth: "1200px", margin: "0 auto", display: "flex", gap: "20px", alignItems: "center", flexWrap: "wrap" }}>
          <div style={{
            width: "72px",
            height: "72px",
            borderRadius: "var(--app-radius, 12px)",
            background: "var(--brand-primary, #123f35)",
            color: "#fff",
            display: "grid",
            placeItems: "center",
            fontSize: "24px",
            fontWeight: 800,
            overflow: "hidden",
            boxShadow: "0 4px 10px rgba(0,0,0,0.06)"
          }}>
            {business.logoUrl && !brandLogoFailed ? (
              <img src={business.logoUrl} alt={business.name} onError={() => setBrandLogoFailed(true)} style={{ width: "100%", height: "100%", objectFit: "cover" }} />
            ) : (
              business.name.slice(0, 2).toUpperCase()
            )}
          </div>
          <div>
            <h1 style={{ fontSize: "1.6em", fontWeight: 800, margin: "0 0 4px 0" }}>{business.name}</h1>
            {business.address && <p style={{ margin: 0, fontSize: "0.9em", color: "var(--text-muted, #6d7e77)" }}>📍 {business.address}</p>}
          </div>
        </div>
      </header>

      {/* Main Container */}
      <main style={{ maxWidth: "1200px", margin: "30px auto 0 auto", padding: "0 20px", display: "grid", gridTemplateColumns: "1fr", gap: "30px" }}>
        {/* Search & Categories Bar */}
        <section className="card" style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
          <div style={{ position: "relative", width: "100%" }}>
            <Search style={{ position: "absolute", left: "14px", top: "50%", transform: "translateY(-50%)", color: "var(--text-muted, #6d7e77)" }} size={18} />
            <input
              type="text"
              placeholder="Buscar productos en el catálogo..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              style={{
                width: "100%",
                height: "46px",
                paddingLeft: "44px",
                border: "1px solid var(--border, #d6e0da)",
                borderRadius: "var(--app-radius, 12px)",
                background: "var(--app-surface, #fff)",
                fontFamily: "inherit",
                fontSize: "0.95em"
              }}
            />
          </div>

          {categories.length > 0 && (
            <div style={{ display: "flex", gap: "8px", overflowX: "auto", paddingBottom: "4px", flexWrap: "wrap" }}>
              <button
                onClick={() => setSelectedCategoryId(null)}
                style={{
                  height: "36px",
                  padding: "0 16px",
                  borderRadius: "20px",
                  border: "none",
                  fontWeight: 700,
                  fontSize: "0.85em",
                  cursor: "pointer",
                  background: selectedCategoryId === null ? "var(--brand-primary, #123f35)" : "var(--border, #d6e0da)",
                  color: selectedCategoryId === null ? "#fff" : "var(--app-text, #17362e)"
                }}
              >
                Todos
              </button>
              {categories.map((cat) => (
                <button
                  key={cat.id}
                  onClick={() => setSelectedCategoryId(cat.id)}
                  style={{
                    height: "36px",
                    padding: "0 16px",
                    borderRadius: "20px",
                    border: "none",
                    fontWeight: 700,
                    fontSize: "0.85em",
                    cursor: "pointer",
                    background: selectedCategoryId === cat.id ? "var(--brand-primary, #123f35)" : "var(--border, #d6e0da)",
                    color: selectedCategoryId === cat.id ? "#fff" : "var(--app-text, #17362e)"
                  }}
                >
                  {cat.name}
                </button>
              ))}
            </div>
          )}
        </section>

        {/* Product Grid */}
        <section>
          {filteredProducts.length === 0 ? (
            <div className="card" style={{ textAlign: "center", padding: "60px 20px", background: "var(--app-surface, #fff)" }}>
              <PackageOpen size={48} style={{ color: "var(--text-muted, #6d7e77)", marginBottom: "16px" }} />
              <h3 style={{ fontSize: "1.2em", fontWeight: 700, margin: "0 0 6px 0" }}>No se encontraron productos</h3>
              <p style={{ color: "var(--text-muted, #6d7e77)", fontSize: "0.9m" }}>Prueba modificando los filtros o escribiendo otra búsqueda.</p>
            </div>
          ) : (
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(250px, 1fr))", gap: "20px" }}>
              {filteredProducts.map((p) => (
                <article key={p.variantId} className="card" style={{ display: "flex", flexDirection: "column", height: "100%", padding: "16px", background: "var(--app-surface, #fff)" }}>
                  {/* Image Container */}
                  <div style={{
                    width: "100%",
                    height: "200px",
                    borderRadius: "var(--app-radius, 12px)",
                    background: "#f0f2f0",
                    display: "grid",
                    placeItems: "center",
                    position: "relative",
                    overflow: "hidden",
                    marginBottom: "12px",
                    border: "1px solid var(--border, #d6e0da)"
                  }}>
                    {p.imageUrl ? (
                      <img src={p.imageUrl} alt={p.productName} style={{ width: "100%", height: "100%", objectFit: "cover" }} />
                    ) : (
                      <PackageOpen size={36} style={{ color: "#bcd0c9" }} />
                    )}
                    {p.stock <= 0 && (
                      <div style={{
                        position: "absolute",
                        top: "10px",
                        left: "10px",
                        background: "#a53c32",
                        color: "#fff",
                        padding: "4px 10px",
                        borderRadius: "15px",
                        fontSize: "0.75em",
                        fontWeight: 700
                      }}>
                        Agotado
                      </div>
                    )}
                  </div>

                  {/* Product Details */}
                  <div style={{ display: "flex", flexDirection: "column", flexGrow: 1 }}>
                    {p.categoryName && (
                      <small style={{ color: "var(--brand-primary, #123f35)", fontWeight: 800, textTransform: "uppercase", fontSize: "0.75em", letterSpacing: "0.05em", marginBottom: "4px" }}>
                        {p.categoryName}
                      </small>
                    )}
                    <h3 style={{ fontSize: "1em", fontWeight: 700, margin: "0 0 4px 0", lineHeight: "1.4" }}>
                      {p.productName}
                    </h3>
                    {p.variantName !== "Única" && (
                      <span style={{ display: "inline-flex", fontSize: "0.8em", color: "var(--text-muted, #6d7e77)", background: "var(--border, #d6e0da)", padding: "2px 8px", borderRadius: "10px", width: "fit-content", marginBottom: "8px" }}>
                        Talla/Var: {p.variantName}
                      </span>
                    )}
                    
                    <div style={{ marginTop: "auto", display: "flex", justifyContent: "space-between", alignItems: "center", paddingTop: "12px", borderTop: "1px solid var(--border, #d6e0da)" }}>
                      <strong style={{ fontSize: "1.25em", color: "var(--brand-primary, #123f35)", fontWeight: 800 }}>
                        {money.format(p.price)}
                      </strong>
                      <button
                        className="button primary"
                        disabled={p.stock <= 0}
                        onClick={() => addToCart(p)}
                        style={{
                          height: "36px",
                          padding: "0 14px",
                          borderRadius: "var(--app-radius, 12px)",
                          fontSize: "0.8em",
                          fontWeight: 700,
                          cursor: p.stock <= 0 ? "not-allowed" : "pointer"
                        }}
                      >
                        Añadir
                      </button>
                    </div>
                  </div>
                </article>
              ))}
            </div>
          )}
        </section>
      </main>

      {/* Floating Shopping Cart Trigger */}
      {totalItemsCount > 0 && (
        <button
          onClick={() => setIsCartOpen(true)}
          style={{
            position: "fixed",
            bottom: "24px",
            right: "24px",
            width: "60px",
            height: "60px",
            borderRadius: "50%",
            background: "var(--brand-primary, #123f35)",
            color: "#fff",
            border: "none",
            display: "grid",
            placeItems: "center",
            boxShadow: "0 8px 24px rgba(18, 63, 53, 0.3)",
            cursor: "pointer",
            zIndex: 100,
            transform: "scale(1)",
            transition: "transform 0.2s"
          }}
        >
          <div style={{ position: "relative" }}>
            <ShoppingCart size={24} />
            <span style={{
              position: "absolute",
              top: "-8px",
              right: "-8px",
              background: "var(--brand-accent, #f5c45e)",
              color: "#1e1e1e",
              fontSize: "0.75em",
              fontWeight: 800,
              width: "20px",
              height: "20px",
              borderRadius: "50%",
              display: "grid",
              placeItems: "center",
              boxShadow: "0 2px 5px rgba(0,0,0,0.15)"
            }}>
              {totalItemsCount}
            </span>
          </div>
        </button>
      )}

      {/* Cart Slider Drawer */}
      {isCartOpen && (
        <div style={{ position: "fixed", top: 0, left: 0, right: 0, bottom: 0, background: "rgba(0,0,0,0.4)", backdropFilter: "blur(4px)", zIndex: 1000, display: "flex", justifyContent: "flex-end" }}>
          {/* Drawer Body */}
          <section style={{
            width: "min(440px, 100%)",
            height: "100%",
            background: "var(--app-surface, #fff)",
            boxShadow: "-10px 0 30px rgba(0,0,0,0.1)",
            display: "flex",
            flexDirection: "column",
            animation: "slideIn 0.25s ease-out"
          }}>
            <style>{`@keyframes slideIn { from { transform: translateX(100%); } to { transform: translateX(0); } }`}</style>
            
            {/* Drawer Header */}
            <div style={{ padding: "20px", borderBottom: "1px solid var(--border, #d6e0da)", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <h2 style={{ fontSize: "1.25em", fontWeight: 800, margin: 0, display: "flex", gap: "8px", alignItems: "center" }}>
                <ShoppingCart size={20} /> Mi pedido ({totalItemsCount})
              </h2>
              <button onClick={() => setIsCartOpen(false)} style={{ border: "none", background: "none", cursor: "pointer", color: "var(--text-muted, #6d7e77)" }}>
                <X size={20} />
              </button>
            </div>

            {/* Cart Items List */}
            <div style={{ flexGrow: 1, overflowY: "auto", padding: "20px", display: "flex", flexDirection: "column", gap: "16px" }}>
              {cart.map((item) => (
                <div key={item.product.variantId} style={{ display: "flex", gap: "12px", borderBottom: "1px solid #f5f6f8", paddingBottom: "16px" }}>
                  <div style={{
                    width: "56px",
                    height: "56px",
                    borderRadius: "8px",
                    background: "#f0f2f0",
                    display: "grid",
                    placeItems: "center",
                    border: "1px solid var(--border, #d6e0da)",
                    overflow: "hidden",
                    flexShrink: 0
                  }}>
                    {item.product.imageUrl ? (
                      <img src={item.product.imageUrl} alt={item.product.productName} style={{ width: "100%", height: "100%", objectFit: "cover" }} />
                    ) : (
                      <PackageOpen size={20} style={{ color: "#bcd0c9" }} />
                    )}
                  </div>
                  <div style={{ flexGrow: 1 }}>
                    <h4 style={{ fontSize: "0.95em", fontWeight: 700, margin: "0 0 2px 0", lineHeight: "1.3" }}>
                      {item.product.productName}
                    </h4>
                    {item.product.variantName !== "Única" && (
                      <span style={{ fontSize: "0.75em", color: "var(--text-muted, #6d7e77)", display: "block", marginBottom: "6px" }}>
                        Var: {item.product.variantName}
                      </span>
                    )}
                    <strong style={{ fontSize: "1em", color: "var(--brand-primary, #123f35)", fontWeight: 700, display: "block", marginBottom: "8px" }}>
                      {money.format(item.product.price)}
                    </strong>
                    
                    {/* Quantity Selector */}
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                      <div style={{ display: "flex", alignItems: "center", border: "1px solid var(--border, #d6e0da)", borderRadius: "6px", overflow: "hidden", height: "30px" }}>
                        <button onClick={() => updateQuantity(item.product.variantId, -1)} style={{ border: "none", background: "none", cursor: "pointer", width: "30px", height: "100%", display: "grid", placeItems: "center", color: "var(--text-muted, #6d7e77)" }}>
                          <Minus size={12} />
                        </button>
                        <span style={{ width: "34px", textAlign: "center", fontSize: "0.85em", fontWeight: 700 }}>
                          {item.quantity}
                        </span>
                        <button onClick={() => updateQuantity(item.product.variantId, 1)} style={{ border: "none", background: "none", cursor: "pointer", width: "30px", height: "100%", display: "grid", placeItems: "center", color: "var(--text-muted, #6d7e77)" }}>
                          <Plus size={12} />
                        </button>
                      </div>
                      <button onClick={() => removeFromCart(item.product.variantId)} style={{ border: "none", background: "none", cursor: "pointer", color: "#a53c32", display: "grid", placeItems: "center" }}>
                        <Trash2 size={16} />
                      </button>
                    </div>
                  </div>
                </div>
              ))}
            </div>

            {/* Cart Footer */}
            <div style={{ padding: "20px", borderTop: "1px solid var(--border, #d6e0da)", background: "var(--app-background, #f5f6f8)" }}>
              <div style={{ display: "flex", justifyContent: "space-between", fontSize: "1.1em", fontWeight: 800, margin: "0 0 16px 0" }}>
                <span>Total estimado:</span>
                <span style={{ color: "var(--brand-primary, #123f35)" }}>{money.format(totalAmount)}</span>
              </div>
              <button
                onClick={sendOrderToWhatsApp}
                className="button primary"
                style={{
                  width: "100%",
                  height: "48px",
                  borderRadius: "var(--app-radius, 12px)",
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "center",
                  gap: "8px",
                  fontSize: "0.95em",
                  fontWeight: 800,
                  cursor: "pointer"
                }}
              >
                <Send size={16} /> Hacer Pedido por WhatsApp
              </button>
            </div>
          </section>
        </div>
      )}

      {/* PLG Marketing Footer for Esencial/Negocio Plans */}
      {isEsencialOrNegocio && (
        <footer style={{
          position: "fixed",
          bottom: 0, left: 0, right: 0,
          background: "var(--brand-primary, #123f35)",
          color: "#fff",
          padding: "10px 20px",
          textAlign: "center",
          fontSize: "0.85em",
          zIndex: 50,
          boxShadow: "0 -4px 10px rgba(0,0,0,0.1)",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          gap: "8px",
          flexWrap: "wrap"
        }}>
          <strong>⚡ Creado gratis con Véndeme Fácil</strong>
          <span style={{ color: "var(--brand-accent, #f5c45e)" }}>•</span>
          <span>¿Quieres un catálogo digital autogenerado para tu tienda?</span>
          <a
            href="/"
            style={{
              color: "var(--brand-accent, #f5c45e)",
              fontWeight: 800,
              textDecoration: "underline",
              cursor: "pointer"
            }}
          >
            Pruébalo gratis aquí
          </a>
        </footer>
      )}
    </div>
  );
}
