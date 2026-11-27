import { Activity, BarChart3, LogOut, Moon, Search, Server, Sun } from 'lucide-react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { clearAuth, getStoredUser } from '../api/client';
import { useTheme } from '../context/ThemeContext';

export function Layout() {
  const navigate = useNavigate();
  const user = getStoredUser();
  const { theme, setTheme } = useTheme();

  const handleLogout = () => {
    clearAuth();
    navigate('/login');
  };

  return (
    <div className="tm-app-shell min-h-screen flex flex-col bg-slate-950 text-slate-100">
      <header className="relative h-14 border-b border-slate-800 bg-slate-900/90 backdrop-blur sticky top-0 z-30 px-4 flex items-center justify-between">
        <div className="flex items-center gap-6">
          <div className="flex items-center gap-2 font-semibold text-slate-100 tracking-tight">
            <div className="w-8 h-8 rounded-lg bg-emerald-500/15 border border-emerald-500/30 flex items-center justify-center text-emerald-400">
              <Activity className="w-4 h-4" />
            </div>
            <span>Trace Monitor</span>
          </div>

          <nav className="flex items-center gap-1">
            <NavLink
              to="/"
              end
              className={({ isActive }) =>
                `px-3 py-1.5 rounded-md text-sm font-medium flex items-center gap-2 transition-colors ${
                  isActive
                    ? 'bg-slate-800 text-emerald-400'
                    : 'text-slate-400 hover:text-slate-100 hover:bg-slate-800/50'
                }`
              }
            >
              <Search className="w-4 h-4" />
              Log Explorer
            </NavLink>
            <NavLink
              to="/analytics"
              className={({ isActive }) =>
                `px-3 py-1.5 rounded-md text-sm font-medium flex items-center gap-2 transition-colors ${
                  isActive
                    ? 'bg-slate-800 text-emerald-400'
                    : 'text-slate-400 hover:text-slate-100 hover:bg-slate-800/50'
                }`
              }
            >
              <BarChart3 className="w-4 h-4" />
              Analytics
            </NavLink>
            <NavLink
              to="/applications"
              className={({ isActive }) =>
                `px-3 py-1.5 rounded-md text-sm font-medium flex items-center gap-2 transition-colors ${
                  isActive
                    ? 'bg-slate-800 text-emerald-400'
                    : 'text-slate-400 hover:text-slate-100 hover:bg-slate-800/50'
                }`
              }
            >
              <Server className="w-4 h-4" />
              Applications &amp; Health
            </NavLink>
          </nav>
        </div>

        <div className="flex items-center gap-3">
          {/* Theme Switcher Buttons */}
          <div
            className="flex items-center rounded-lg border border-slate-800 bg-slate-950/90 p-0.5 text-xs"
            role="group"
            aria-label="Theme switcher"
          >
            <button
              type="button"
              onClick={() => setTheme('light')}
              aria-pressed={theme === 'light'}
              title="Switch to colourful light theme"
              className={`px-2.5 py-1 rounded-md flex items-center gap-1.5 font-medium transition-all cursor-pointer ${
                theme === 'light'
                  ? 'bg-gradient-to-r from-amber-400 via-pink-500 to-violet-500 text-white shadow-sm'
                  : 'text-slate-400 hover:text-slate-100'
              }`}
            >
              <Sun className="w-3.5 h-3.5" />
              <span className="hidden sm:inline">Light</span>
            </button>
            <button
              type="button"
              onClick={() => setTheme('dark')}
              aria-pressed={theme === 'dark'}
              title="Switch to dark theme"
              className={`px-2.5 py-1 rounded-md flex items-center gap-1.5 font-medium transition-all cursor-pointer ${
                theme === 'dark'
                  ? 'bg-slate-800 text-emerald-400 shadow-sm'
                  : 'text-slate-400 hover:text-slate-100'
              }`}
            >
              <Moon className="w-3.5 h-3.5" />
              <span className="hidden sm:inline">Dark</span>
            </button>
          </div>

          {user && (
            <span className="text-xs text-slate-400 bg-slate-800/80 border border-slate-700 px-2.5 py-1 rounded-full">
              {user.username}
            </span>
          )}
          <button
            onClick={handleLogout}
            className="text-slate-400 hover:text-slate-100 text-sm flex items-center gap-1.5 px-2.5 py-1.5 rounded-md hover:bg-slate-800 transition-colors cursor-pointer"
            title="Sign out"
          >
            <LogOut className="w-4 h-4" />
            <span className="hidden sm:inline">Sign Out</span>
          </button>
        </div>
      </header>

      <main className="flex-1 flex flex-col">
        <Outlet />
      </main>
    </div>
  );
}
