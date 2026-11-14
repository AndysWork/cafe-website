import { Component } from '@angular/core';
import { RouterModule } from '@angular/router';
import { AdminDashboardComponent } from '../admin-dashboard/admin-dashboard.component';

@Component({
  selector: 'app-manager-dashboard',
  standalone: true,
  imports: [AdminDashboardComponent, RouterModule],
  templateUrl: './manager-dashboard.component.html',
  styleUrls: ['./manager-dashboard.component.scss']
})
export class ManagerDashboardComponent {}
