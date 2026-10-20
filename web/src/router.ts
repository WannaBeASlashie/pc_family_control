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
    { path: '/overview', component: OverviewView, meta: { title: 'Overview' } },
    { path: '/accounts', component: AccountsView, meta: { title: 'Accounts' } },
    { path: '/requests', component: RequestsView, meta: { title: 'Requests' } },
    { path: '/policies', component: PoliciesView, meta: { title: 'Policies' } },
    { path: '/machines', component: MachinesView, meta: { title: 'Machines' } },
    { path: '/records', component: RecordsView, meta: { title: 'Records' } },
    { path: '/login', component: LoginView, meta: { title: 'Sign in' } },
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
