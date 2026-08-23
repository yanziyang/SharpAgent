import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router'
import { ThemeProvider } from '@/shared/theme/theme-provider'
import { createAppRouter } from './app/router'
import './index.css'

const router = createAppRouter()

createRoot(document.querySelector('#root')!).render(
  <StrictMode>
    <ThemeProvider>
      <RouterProvider router={router} />
    </ThemeProvider>
  </StrictMode>,
)
