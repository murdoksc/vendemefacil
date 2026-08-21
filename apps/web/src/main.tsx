import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import App from './App'
import './styles.css'
import './marketing.css'
import './pricing.css'
import './platform-admin.css'
import './subscription.css'
import './subscription-admin.css'
import './admin-shortcut.css'
import './reports.css'
import './users.css'
import './customers.css'
import './cash.css'
import './catalog-extra.css'
import './pos-extra.css'
import './inventory-tools.css'
import './transfer.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
