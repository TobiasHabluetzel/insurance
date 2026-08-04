/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    './Views/**/*.cshtml',
    './Pages/**/*.cshtml',
    './wwwroot/js/**/*.js',
    // Directive templates + controller-generated markup live under
    // wwwroot/app/. Without this glob every utility class that only
    // appears inside a directive template gets purged from site.css.
    './wwwroot/app/**/*.js',
  ],
  theme: {
    extend: {
      colors: {
        brand: {
          purple: 'var(--brand-primary)',
          'purple-dark': 'var(--brand-primary-dark)',
          'purple-card': 'var(--brand-primary-card)',
          'purple-muted': 'var(--brand-primary-muted)',
          blue: 'var(--brand-accent)',
          'blue-light': 'var(--brand-accent-light)',
        },
      },
      fontFamily: {
        sans: ['Inter', 'system-ui', 'sans-serif'],
      },
    },
  },
  plugins: [],
}
