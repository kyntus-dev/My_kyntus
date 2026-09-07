import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { SubService, SubServiceDetail, CreateSubServiceDto, UpdateSubServiceDto } from '../sub-services-module';
import { environment } from '../../../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class SubServiceService {
  private apiUrl = `${environment.apiUrl}/subservices`;

  constructor(private http: HttpClient) {}

  getAllSubServices(): Observable<SubService[]> {
    return this.http.get<SubService[]>(this.apiUrl).pipe(
      catchError(err => throwError(() => err))
    );
  }

  getSubServicesByService(serviceId: number): Observable<SubService[]> {
    return this.http.get<SubService[]>(`${this.apiUrl}/by-service/${serviceId}`).pipe(
      catchError(err => throwError(() => err))
    );
  }

  getSubServiceById(id: number): Observable<SubService> {
    return this.http.get<SubService>(`${this.apiUrl}/${id}`).pipe(
      catchError(err => throwError(() => err))
    );
  }

  getSubServiceDetails(id: number): Observable<SubServiceDetail> {
    return this.http.get<SubServiceDetail>(`${this.apiUrl}/${id}/details`).pipe(
      catchError(err => throwError(() => err))
    );
  }

  createSubService(dto: CreateSubServiceDto): Observable<SubService> {
    return this.http.post<SubService>(this.apiUrl, dto).pipe(
      catchError(err => throwError(() => err))
    );
  }

  updateSubService(id: number, dto: UpdateSubServiceDto): Observable<SubService> {
    return this.http.put<SubService>(`${this.apiUrl}/${id}`, dto).pipe(
      catchError(err => throwError(() => err))
    );
  }

  deleteSubService(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`).pipe(
      catchError(err => throwError(() => err))
    );
  }

  checkCodeUnique(code: string, excludeId?: number): Observable<{ isUnique: boolean }> {
    const params = excludeId ? `?excludeId=${excludeId}` : '';
    return this.http.get<{ isUnique: boolean }>(`${this.apiUrl}/check-code/${code}${params}`).pipe(
      catchError(err => throwError(() => err))
    );
  }
}