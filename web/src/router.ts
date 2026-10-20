import { createRouter, createWebHistory } from 'vue-router'
import { getToken } from '@/api/client'
import OverviewView from '@/views/OverviewView.vue'
import AccountsView from '@/views/AccountsView.vue'
import RequestsView from '@/views/RequestsView.vue'
import PoliciesView from '@/views/PoliciesView.vue'
import MachinesView from '@/views/MachinesView.vue'
import RecordsView from '@/views/RecordsView.vue'
import LoginView from '@/views/LoginView.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', redirect: '/overview' },
    { path: '/overview', component: OverviewView, meta: { titleKey: 'nav.overview' } },
    { path: '/accounts', component: AccountsView, meta: { titleKey: 'nav.accounts' } },
    { path: '/requests', component: RequestsView, meta: { titleKey: 'nav.requests' } },
    { path: '/policies', component: PoliciesView, meta: { titleKey: 'nav.policies' } },
    { path: '/machines', component: MachinesView, meta: { titleKey: 'nav.machines' } },
    { path: '/records', component: RecordsView, meta: { titleKey: 'nav.records' } },
    { path: '/login', component: LoginView, meta: { titleKey: 'login.title' } },
    { path: '/:pathMatch(.*)*', redirect: '/overview' }
  ]
})

router.beforeEach((to) => {
  const authed = !!getToken()
  if (!authed && to.path !== '/login') return '/login'
  if (authed && to.path === '/login') return '/overview'
  return true
})

export default router
